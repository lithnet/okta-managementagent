using System;
using System.Collections;
using System.Collections.Generic;
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
    internal class UserImportProvider : ProducerConsumerImportProvider<IUser>
    {
        private readonly IOktaClient client;
        private readonly GlobalOptions globalOptions;
        private readonly ILogger<UserImportProvider> logger;
        private long userHighestTicks = 0;
        private string initialWatermarkValue;

        public UserImportProvider(OktaClientProvider clientProvider, IOptions<GlobalOptions> globalOptions, ILogger<UserImportProvider> logger)
            : base(logger)
        {
            this.client = clientProvider.GetClient();
            this.globalOptions = globalOptions.Value;
            this.logger = logger;
        }

        public override Task<bool> CanImportAsync(SchemaType type)
        {
            return Task.FromResult(type.Name == "user");
        }

        protected override async Task<AttributeChange> CreateAttributeChangeAsync(SchemaAttribute type, ObjectModificationType modificationType, IUser item, CancellationToken cancellationToken)
        {
            AttributeChange change = null;

            if (type.Name == "provider.name")
            {
                change = AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(item.Credentials.Provider.Name, type.DataType));
            }
            else if (type.Name == "provider.type")
            {
                change = AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(item.Credentials.Provider.Type.Value, type.DataType));
            }
            else if (type.Name == "suspended")
            {
                change = AttributeChange.CreateAttributeAdd(type.Name, item.Status == UserStatus.Suspended);
            }
            else if (type.Name == "enrolledFactors")
            {
                List<object> items = new List<object>();
                await foreach (var factor in item.ListFactors())
                {
                    items.Add($"{factor.Provider}/{factor.FactorType}");
                }

                if (items.Count > 0)
                {
                    change = AttributeChange.CreateAttributeAdd(type.Name, items);
                }
            }
            else if (type.Name == "availableFactors")
            {
                List<object> items = new List<object>();
                await foreach (var factor in item.ListSupportedFactors().WithCancellation(cancellationToken))
                {
                    items.Add($"{factor.Provider}/{factor.FactorType}");
                }

                if (items.Count > 0)
                {
                    change = AttributeChange.CreateAttributeAdd(type.Name, items);
                }
            }
            else
            {
                Resource profile = item.GetProperty<Resource>("profile");

                object value = item.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                if (value != null)
                {
                    if (value is IList list)
                    {
                        IList<object> values = new List<object>();

                        foreach (object item2 in list)
                        {
                            values.Add(TypeConverter.ConvertData(item2, type.DataType));
                        }

                        change = AttributeChange.CreateAttributeAdd(type.Name, values);
                    }
                    else
                    {
                        change = AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType));
                    }
                }
            }

            return change;
        }

        protected override Task<List<AnchorAttribute>> GetAnchorAttributesAsync(IUser item)
        {
            return Task.FromResult(new List<AnchorAttribute> { AnchorAttribute.Create("id", item.Id) });
        }

        protected override Task<string> GetDNAsync(IUser item)
        {
            return Task.FromResult(item.Id);
        }

        protected override Task<ObjectModificationType> GetObjectModificationTypeAsync(IUser item)
        {
            return Task.FromResult(this.GetObjectModificationType(item));
        }

        protected override IAsyncEnumerable<IUser> GetObjectsAsync(string watermark, CancellationToken cancellationToken)
        {
            IAsyncEnumerable<IUser> users;

            this.initialWatermarkValue = watermark;

            if (this.ImportContext.InDelta)
            {
                string value = watermark ?? throw new WarningNoWatermarkException("No watermark was available to perform a delta import of user objects. Please run a full import");

                long ticks = long.Parse(value);
                DateTime dt = new DateTime(ticks);

                string filter = $"(lastUpdated gt \"{dt.ToSmartString()}Z\")";

                if (this.globalOptions.UserDeprovisioningAction == "Delete")
                {
                    filter += " and(status eq \"LOCKED_OUT\" or status eq \"RECOVERY\" or status eq \"STAGED\" or status eq \"PROVISIONED\" or status eq \"ACTIVE\" or status eq \"PASSWORD_EXPIRED\" or status eq \"DEPROVISIONED\" or status eq \"SUSPENDED\")";
                }

                users = this.client.Users.ListUsers(null, null, OktaMAConfigSection.Configuration.UserListPageSize, filter);
            }
            else
            {
                string filter = null;

                if (this.globalOptions.UserDeprovisioningAction == "Delete")
                {
                    filter = "(status eq \"LOCKED_OUT\" or status eq \"RECOVERY\" or status eq \"STAGED\" or status eq \"PROVISIONED\" or status eq \"ACTIVE\" or status eq \"PASSWORD_EXPIRED\" or status eq \"DEPROVISIONED\" or status eq \"SUSPENDED\")";
                }

                users = this.client.Users.ListUsers(null, null, OktaMAConfigSection.Configuration.UserListPageSize, filter);
            }

            return users;
        }

        protected override Task PrepareObjectForImportAsync(IUser item, CancellationToken cancellationToken)
        {
            if (item.LastUpdated.HasValue)
            {
                InterlockedHelpers.InterlockedMax(ref this.userHighestTicks, item.LastUpdated.Value.Ticks);
            }

            return Task.CompletedTask;
        }

        private ObjectModificationType GetObjectModificationType(IUser user)
        {
            if (!this.ImportContext.InDelta)
            {
                if (this.globalOptions.UserDeprovisioningAction != "Delete")
                {
                    if ((user.Status?.Value == UserStatus.Deprovisioned ||
                         user.TransitioningToStatus?.Value == UserStatus.Deprovisioned) &&
                        user.TransitioningToStatus?.Value != UserStatus.Provisioned)
                    {
                        this.logger.LogTrace($"Discarding {user.Id} as status is deprovisioned");
                        return ObjectModificationType.None;
                    }
                }

                return ObjectModificationType.Add;
            }

            if (this.globalOptions.UserDeprovisioningAction != "Delete")
            {
                if ((user.Status?.Value == UserStatus.Deprovisioned ||
                     user.TransitioningToStatus?.Value == UserStatus.Deprovisioned) &&
                    user.TransitioningToStatus?.Value != UserStatus.Provisioned)
                {
                    return ObjectModificationType.Delete;
                }
            }

            return ObjectModificationType.Replace;
        }

        public override Task<string> GetOutboundWatermark(SchemaType type, CancellationToken cancellationToken)
        {
            string wmv;

            if (this.userHighestTicks <= 0)
            {
                wmv = this.initialWatermarkValue;
            }
            else
            {
                wmv = this.userHighestTicks.ToString();
            }

            return Task.FromResult(wmv);
        }
    }
}