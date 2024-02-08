using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
        private readonly IOktaClient client;
        private readonly ILogger<GroupImportProvider> logger;
        private readonly GlobalOptions globalOptions;
        private long groupUpdateHighestTicks = 0;
        private long groupMemberUpdateHighestTicks = 0;
        private GroupWatermark inboundWatermark;

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

        protected override async Task<AttributeChange> CreateAttributeChangeAsync(SchemaAttribute type, ObjectModificationType modificationType, IGroup item, CancellationToken cancellationToken)
        {
            if (type.Name == "member")
            {
                IList<object> members = new List<object>();

                var items = this.client.GetCollection<User>($"/api/v1/groups/{item.Id}/skinny_users");

                await items.ForEachAsync(u => members.Add(u.Id), cancellationToken).ConfigureAwait(false);

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

        protected override IAsyncEnumerable<IGroup> GetObjectsAsync(string watermark, CancellationToken cancellationToken)
        {
            if (watermark != null)
            {
                try
                {
                    this.inboundWatermark = JsonSerializer.Deserialize<GroupWatermark>(watermark);
                }
                catch (Exception ex)
                {
                    this.logger.LogError(ex, "Unable to deserialize watermark data");
                }
            }

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
                if (this.inboundWatermark?.LastUpdated == null || this.inboundWatermark?.LastMembershipUpdated == null)
                {
                    throw new WarningNoWatermarkException("The watermark was not present for the group import operation. Please run a full import.");
                }

                filter = $"({filter}) AND (lastUpdated gt \"{this.inboundWatermark.LastUpdated.ToSmartString()}Z\" or lastMembershipUpdated gt \"{this.inboundWatermark.LastMembershipUpdated.ToSmartString()}Z\")";
            }

            return this.client.Groups.ListGroups(null, filter, null, OktaMAConfigSection.Configuration.GroupListPageSize, null, null);
        }

        protected override Task<ObjectModificationType> GetObjectModificationTypeAsync(IGroup item)
        {
            return Task.FromResult(this.GetObjectModificationType(item));
        }

        protected override Task PrepareObjectForImportAsync(IGroup item, CancellationToken cancellationToken)
        {
            if (item.LastUpdated.HasValue)
            {
                InterlockedHelpers.InterlockedMax(ref this.groupUpdateHighestTicks, item.LastUpdated.Value.Ticks);
            }

            if (item.LastMembershipUpdated.HasValue)
            {
                InterlockedHelpers.InterlockedMax(ref this.groupMemberUpdateHighestTicks, item.LastMembershipUpdated.Value.Ticks);
            }

            return Task.CompletedTask;
        }

        public override Task<string> GetOutboundWatermark(SchemaType type, CancellationToken cancellationToken)
        {
            GroupWatermark g = new GroupWatermark()
            {
                LastMembershipUpdated = this.groupMemberUpdateHighestTicks <= 0 ? this.inboundWatermark?.LastMembershipUpdated : new DateTime(this.groupMemberUpdateHighestTicks),
                LastUpdated = this.groupUpdateHighestTicks <= 0 ? this.inboundWatermark?.LastUpdated : new DateTime(this.groupUpdateHighestTicks)
            };

            return Task.FromResult(JsonSerializer.Serialize(g));
        }

        private ObjectModificationType GetObjectModificationType(IGroup group)
        {
            if (!this.ImportContext.InDelta)
            {
                return ObjectModificationType.Add;
            }

            if (group.Created > this.inboundWatermark.LastUpdated)
            {
                return ObjectModificationType.Add;
            }

            return ObjectModificationType.Update;
        }

    }
}
