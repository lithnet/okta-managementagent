using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class GroupImportProvider : IObjectImportProvider
    {
        private const string GroupUpdateKey = "group";
        private const string GroupMemberUpdateKey = "group-member";
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private IImportContext context;
        private IOktaClient client;
        private DateTime? lastGroupUpdateHighWatermark;
        private DateTime? lastGroupMemberUpdateHighWatermark;

        public void Initialize(IImportContext context)
        {
            this.context = context;
            this.client = ((OktaConnectionContext)context.ConnectionContext).Client;
        }

        public void GetCSEntryChanges(SchemaType type)
        {
            AsyncHelper.RunSync(this.GetCSEntryChangesAsync(type), this.context.Token);
        }

        public async Task GetCSEntryChangesAsync(SchemaType type)
        {
            try
            {
                IAsyncEnumerable<IGroup> groups = this.GetGroupEnumerable();
                BufferBlock<IGroup> queue = new BufferBlock<IGroup>();

                Task consumer = this.ConsumeObjects(type, queue);

                // Post source data to the dataflow block.
                await this.ProduceObjects(groups, queue).ConfigureAwait(false);

                // Wait for the consumer to process all data.
                await consumer.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "There was an error importing the group data");
                throw;
            }
        }

        private async Task ProduceObjects(IAsyncEnumerable<IGroup> groups, ITargetBlock<IGroup> target)
        {
            await groups.ForEachAsync(t => target.Post(t));
            target.Complete();
        }

        private async Task ConsumeObjects(SchemaType type, ISourceBlock<IGroup> source)
        {
            long groupUpdateHighestTicks = 0;
            long groupMemberUpdateHighestTicks = 0;


            while (await source.OutputAvailableAsync())
            {
                IGroup group = source.Receive();

                try
                {
                    if (group.LastUpdated.HasValue)
                    {
                        AsyncHelper.InterlockedMax(ref groupUpdateHighestTicks, group.LastUpdated.Value.Ticks);
                    }

                    if (group.LastMembershipUpdated.HasValue)
                    {
                        AsyncHelper.InterlockedMax(ref groupMemberUpdateHighestTicks, group.LastMembershipUpdated.Value.Ticks);
                    }

                    CSEntryChange c = await this.GroupToCSEntryChange(type, group).ConfigureAwait(false);

                    if (c != null)
                    {
                        this.context.ImportItems.Add(c, this.context.Token);
                    }
                }
                catch (Exception ex)
                {
                    GroupImportProvider.logger.Error(ex);
                    CSEntryChange csentry = CSEntryChange.Create();
                    csentry.DN = group.Id;
                    csentry.ErrorCodeImport = MAImportError.ImportErrorCustomContinueRun;
                    csentry.ErrorDetail = ex.StackTrace;
                    csentry.ErrorName = ex.Message;
                    this.context.ImportItems.Add(csentry, this.context.Token);
                }

                this.context.Token.ThrowIfCancellationRequested();
            }

            string wmv;

            if (groupUpdateHighestTicks <= 0)
            {
                wmv = this.context.IncomingWatermark[GroupUpdateKey].Value;
            }
            else
            {
                wmv = groupUpdateHighestTicks.ToString();
            }

            this.context.OutgoingWatermark.Add(new Watermark(GroupUpdateKey, wmv, "DateTime"));

            if (groupMemberUpdateHighestTicks <= 0)
            {
                wmv = this.context.IncomingWatermark[GroupMemberUpdateKey].Value;
            }
            else
            {
                wmv = groupMemberUpdateHighestTicks.ToString();
            }

            this.context.OutgoingWatermark.Add(new Watermark(GroupMemberUpdateKey, wmv, "DateTime"));
        }

        private async Task<CSEntryChange> GroupToCSEntryChange(SchemaType schemaType, IGroup group)
        {
            Resource profile = group.GetProperty<Resource>("profile");
            logger.Trace($"Creating CSEntryChange for group {group.Id}");

            ObjectModificationType modType = this.GetObjectModificationType(group);

            if (modType == ObjectModificationType.None)
            {
                return null;
            }

            CSEntryChange c = CSEntryChange.Create();
            c.ObjectType = "group";
            c.ObjectModificationType = modType;
            c.AnchorAttributes.Add(AnchorAttribute.Create("id", group.Id));
            c.DN = group.Id;

            if (modType == ObjectModificationType.Delete)
            {
                return c;
            }

            foreach (SchemaAttribute type in schemaType.Attributes)
            {
                if (type.Name == "member")
                {
                    IList<object> members = new List<object>();

                    var items = ((OktaConnectionContext)this.context.ConnectionContext).Client.GetCollection<User>($"/api/v1/groups/{group.Id}/skinny_users");

                    await items.ForEachAsync(u => members.Add(u.Id)).ConfigureAwait(false);

                    if (modType == ObjectModificationType.Update)
                    {
                        if (members.Count == 0)
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeDelete(type.Name));
                        }
                        else
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeReplace(type.Name, members));
                        }
                    }
                    else
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, members));
                    }

                    continue;
                }

                object value = group.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                if (modType == ObjectModificationType.Update)
                {
                    if (value == null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeDelete(type.Name));
                    }
                    else
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeReplace(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                    }
                }
                else
                {
                    if (value != null)
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                    }
                }
            }

            return c;
        }

        private DateTime GetLastHighWatermarkGroup()
        {
            return this.GetLastHighWatermark(GroupUpdateKey);
        }

        private DateTime GetLastHighWatermarkGroupMember()
        {
            return this.GetLastHighWatermark(GroupMemberUpdateKey);
        }

        private DateTime GetLastHighWatermark(string key)
        {
            if (this.context.IncomingWatermark == null)
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
            }

            if (!this.context.IncomingWatermark.Contains(key))
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the group object type");
            }

            string value = this.context.IncomingWatermark[key].Value;
            long ticks = long.Parse(value);
            return new DateTime(ticks);
        }

        private IAsyncEnumerable<IGroup> GetGroupEnumerable()
        {
            List<string> filterConditions = new List<string>();

            if (this.context.ConfigParameters[ConfigParameterNames.IncludeBuiltInGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"BUILT_IN\")");
            }

            if (this.context.ConfigParameters[ConfigParameterNames.IncludeAppGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"APP_GROUP\")");
            }

            filterConditions.Add("(type eq \"OKTA_GROUP\")");

            string filter = string.Join(" OR ", filterConditions);

            if (this.context.InDelta)
            {
                this.lastGroupUpdateHighWatermark = this.GetLastHighWatermarkGroup();
                this.lastGroupMemberUpdateHighWatermark = this.GetLastHighWatermarkGroupMember();

                filter = $"({filter}) AND (lastUpdated gt \"{this.lastGroupUpdateHighWatermark.ToSmartString()}Z\" or lastMembershipUpdated gt \"{this.lastGroupMemberUpdateHighWatermark.ToSmartString()}Z\")";
            }

            return this.client.Groups.ListGroups(null, filter, null, OktaMAConfigSection.Configuration.GroupListPageSize);
        }

        private ObjectModificationType GetObjectModificationType(IGroup group)
        {
            if (!this.context.InDelta)
            {
                return ObjectModificationType.Add;
            }

            if (group.Created > this.lastGroupUpdateHighWatermark)
            {
                return ObjectModificationType.Add;
            }

            return ObjectModificationType.Update;
        }

        public bool CanImport(SchemaType type)
        {
            return type.Name == "group";
        }
    }
}
