using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
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

        private DateTime? lastGroupUpdateHighWatermark;

        private DateTime? lastGroupMemberUpdateHighWatermark;

        private static Logger logger = LogManager.GetCurrentClassLogger();

        public void GetCSEntryChanges(ImportContext context, SchemaType type)
        {
            AsyncHelper.RunSync(this.GetCSEntryChangesAsync(context, type), context.CancellationTokenSource.Token);
        }

        public async Task GetCSEntryChangesAsync(ImportContext context, SchemaType type)
        {
            try
            {
                IAsyncEnumerable<IGroup> groups = this.GetGroupEnumerable(context.InDelta, context.ConfigParameters, context.IncomingWatermark, ((OktaConnectionContext)context.ConnectionContext).Client);
                BufferBlock<IGroup> queue = new BufferBlock<IGroup>();

                Task consumer = this.ConsumeObjects(context, type, queue);

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

        private async Task ConsumeObjects(ImportContext context, SchemaType type, ISourceBlock<IGroup> source)
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

                    CSEntryChange c = await this.GroupToCSEntryChange(context, type, group).ConfigureAwait(false);

                    if (c != null)
                    {
                        context.ImportItems.Add(c, context.CancellationTokenSource.Token);
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
                    context.ImportItems.Add(csentry, context.CancellationTokenSource.Token);
                }

                context.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            }

            string wmv;

            if (groupUpdateHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark[GroupUpdateKey].Value;
            }
            else
            {
                wmv = groupUpdateHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark(GroupUpdateKey, wmv, "DateTime"));

            if (groupMemberUpdateHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark[GroupMemberUpdateKey].Value;
            }
            else
            {
                wmv = groupMemberUpdateHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark(GroupMemberUpdateKey, wmv, "DateTime"));
        }

        private async Task<CSEntryChange> GroupToCSEntryChange(ImportContext context, SchemaType schemaType, IGroup group)
        {
            Resource profile = group.GetProperty<Resource>("profile");
            logger.Trace($"Creating CSEntryChange for group {group.Id}");

            ObjectModificationType modType = this.GetObjectModificationType(group, context.InDelta);

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

                    var items = ((OktaConnectionContext) context.ConnectionContext).Client.GetCollection<User>($"/api/v1/groups/{group.Id}/skinny_users");

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

        private DateTime GetLastHighWatermarkGroup(WatermarkKeyedCollection importState)
        {
            return this.GetLastHighWatermark(importState, GroupUpdateKey);
        }

        private DateTime GetLastHighWatermarkGroupMember(WatermarkKeyedCollection importState)
        {
            return this.GetLastHighWatermark(importState, GroupMemberUpdateKey);
        }

        private DateTime GetLastHighWatermark(WatermarkKeyedCollection importState, string key)
        {
            if (importState == null)
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
            }

            if (!importState.Contains(key))
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the group object type");
            }

            string value = importState[key].Value;
            long ticks = long.Parse(value);
            return new DateTime(ticks);
        }

        private IAsyncEnumerable<IGroup> GetGroupEnumerable(bool inDelta, KeyedCollection<string, ConfigParameter> configParameters, WatermarkKeyedCollection importState, IOktaClient client)
        {
            List<string> filterConditions = new List<string>();

            if (configParameters[ConfigParameterNames.IncludeBuiltInGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"BUILT_IN\")");
            }

            if (configParameters[ConfigParameterNames.IncludeAppGroups].Value == "1")
            {
                filterConditions.Add("(type eq \"APP_GROUP\")");
            }

            filterConditions.Add("(type eq \"OKTA_GROUP\")");

            string filter = string.Join(" OR ", filterConditions);

            if (inDelta)
            {
                this.lastGroupUpdateHighWatermark = this.GetLastHighWatermarkGroup(importState);
                this.lastGroupMemberUpdateHighWatermark = this.GetLastHighWatermarkGroupMember(importState);

                filter = $"({filter}) AND (lastUpdated gt \"{this.lastGroupUpdateHighWatermark.ToSmartString()}Z\" or lastMembershipUpdated gt \"{this.lastGroupMemberUpdateHighWatermark.ToSmartString()}Z\")";
            }

            return client.Groups.ListGroups(null, filter, null, OktaMAConfigSection.Configuration.GroupListPageSize);
        }

        private ObjectModificationType GetObjectModificationType(IGroup group, bool inDelta)
        {
            if (!inDelta)
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
