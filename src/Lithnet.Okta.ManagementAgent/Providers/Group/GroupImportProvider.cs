using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class GroupImportProvider : ProducerConsumerImportProvider<IGroup>
    {
        private const string GroupUpdateKey = "group";
        private const string GroupMemberUpdateKey = "group-member";

        private readonly IOktaClient client;
        private readonly ILogger<GroupImportProvider> logger;
        private readonly GlobalOptions globalOptions;

        private DateTime? lastGroupUpdateHighWatermark;
        private DateTime? lastGroupMemberUpdateHighWatermark;
        private long groupUpdateHighestTicks = 0;
        private long groupMemberUpdateHighestTicks = 0;

        public GroupImportProvider(OktaClientProvider oktaClientProvider, IOptions<GlobalOptions> globalOptions, ILogger<GroupImportProvider> logger) : base(logger)
        {
            this.client = oktaClientProvider.GetClient();
            this.logger = logger;
            this.globalOptions = globalOptions.Value;
        }


        public override Task<bool> CanImportAsync(SchemaType type)
        {
            return Task.FromResult(type.Name == "group");
        }

        protected override Task<List<AnchorAttribute>> GetAnchorAttributesAsync(IGroup item)
        {
            return Task.FromResult(new List<AnchorAttribute>() { AnchorAttribute.Create("id", item.Id) });
        }

        protected override async Task<AttributeChange> CreateAttributeChangeAsync(SchemaAttribute type, ObjectModificationType modificationType, IGroup item)
        {
            if (type.Name == "member")
            {
                IList<object> members = new List<object>();

                var items = this.client.GetCollection<User>($"/api/v1/groups/{item.Id}/skinny_users");

                await items.ForEachAsync(u => members.Add(u.Id)).ConfigureAwait(false);

                if (modificationType == ObjectModificationType.Update)
                {
                    if (members.Count == 0)
                    {
                        return AttributeChange.CreateAttributeDelete(type.Name);
                    }
                    else
                    {
                        return AttributeChange.CreateAttributeReplace(type.Name, members);
                    }
                }
                else
                {
                    return AttributeChange.CreateAttributeAdd(type.Name, members);
                }
            }
            else
            {
                Resource profile = item.GetProperty<Resource>("profile");

                object value = item.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                if (modificationType == ObjectModificationType.Update)
                {
                    if (value == null)
                    {
                        return AttributeChange.CreateAttributeDelete(type.Name);
                    }
                    else
                    {
                        return AttributeChange.CreateAttributeReplace(type.Name, TypeConverter.ConvertData(value, type.DataType));
                    }
                }
                else
                {
                    if (value != null)
                    {
                        return AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType));
                    }
                }
            }

            return null;
        }

        protected override Task<string> GetDNAsync(IGroup item)
        {
            return Task.FromResult(item.Id);
        }

        protected override IAsyncEnumerable<IGroup> GetObjects()
        {
            List<string> filterConditions = new List<string>();

            if (this.globalOptions.IncludeBuiltInGroups)
            {
                filterConditions.Add("(type eq \"BUILT_IN\")");
            }

            if (this.globalOptions.IncludeAppGroups)
            {
                filterConditions.Add("(type eq \"APP_GROUP\")");
            }

            filterConditions.Add("(type eq \"OKTA_GROUP\")");

            string filter = string.Join(" OR ", filterConditions);

            if (this.ImportContext.InDelta)
            {
                this.lastGroupUpdateHighWatermark = this.GetLastHighWatermarkGroup();
                this.lastGroupMemberUpdateHighWatermark = this.GetLastHighWatermarkGroupMember();

                filter = $"({filter}) AND (lastUpdated gt \"{this.lastGroupUpdateHighWatermark.ToSmartString()}Z\" or lastMembershipUpdated gt \"{this.lastGroupMemberUpdateHighWatermark.ToSmartString()}Z\")";
            }

            return this.client.Groups.ListGroups(null, filter, null, OktaMAConfigSection.Configuration.GroupListPageSize, null, null);
        }

        protected override Task<ObjectModificationType> GetObjectModificationTypeAsync(IGroup item)
        {
            return Task.FromResult(this.GetObjectModificationType(item));
        }

        protected override Task PrepareObjectForImportAsync(IGroup item)
        {
            if (item.LastUpdated.HasValue)
            {
                AsyncHelper.InterlockedMax(ref this.groupUpdateHighestTicks, item.LastUpdated.Value.Ticks);
            }

            if (item.LastMembershipUpdated.HasValue)
            {
                AsyncHelper.InterlockedMax(ref this.groupMemberUpdateHighestTicks, item.LastMembershipUpdated.Value.Ticks);
            }

            return Task.CompletedTask;
        }

        protected override Task OnCompleteConsumerAsync()
        {
            string wmv;

            if (this.groupUpdateHighestTicks <= 0)
            {
                wmv = this.ImportContext.IncomingWatermark[GroupUpdateKey].Value;
            }
            else
            {
                wmv = this.groupUpdateHighestTicks.ToString();
            }

            this.ImportContext.OutgoingWatermark.Add(new Watermark(GroupUpdateKey, wmv, "DateTime"));

            if (this.groupMemberUpdateHighestTicks <= 0)
            {
                wmv = this.ImportContext.IncomingWatermark[GroupMemberUpdateKey].Value;
            }
            else
            {
                wmv = this.groupMemberUpdateHighestTicks.ToString();
            }

            this.ImportContext.OutgoingWatermark.Add(new Watermark(GroupMemberUpdateKey, wmv, "DateTime"));

            return Task.CompletedTask;
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
            if (this.ImportContext.IncomingWatermark == null)
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
            }

            if (!this.ImportContext.IncomingWatermark.Contains(key))
            {
                throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the group object type");
            }

            string value = this.ImportContext.IncomingWatermark[key].Value;
            long ticks = long.Parse(value);
            return new DateTime(ticks);
        }

        private ObjectModificationType GetObjectModificationType(IGroup group)
        {
            if (!this.ImportContext.InDelta)
            {
                return ObjectModificationType.Add;
            }

            if (group.Created > this.lastGroupUpdateHighWatermark)
            {
                return ObjectModificationType.Add;
            }

            return ObjectModificationType.Update;
        }

    }
}
