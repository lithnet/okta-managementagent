using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;
using System.Threading.Tasks.Dataflow;

namespace Lithnet.Okta.ManagementAgent
{
    internal class UserImportProvider : IObjectImportProvider
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public void GetCSEntryChanges(ImportContext context, SchemaType type)
        {
            AsyncHelper.RunSync(this.GetCSEntryChangesAsync(context, type), context.CancellationTokenSource.Token);
        }

        public async Task GetCSEntryChangesAsync(ImportContext context, SchemaType type)
        {
            try
            {
                IAsyncEnumerable<IUser> users = this.GetUserEnumerable(context.InDelta, context.IncomingWatermark, ((OktaConnectionContext)context.ConnectionContext).Client, context);
                BufferBlock<IUser> queue = new BufferBlock<IUser>();

                Task consumer = this.ConsumeObjects(context, type, queue);

                // Post source data to the dataflow block.
                await this.ProduceObjects(users, queue).ConfigureAwait(false);

                // Wait for the consumer to process all data.
                await consumer.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.Error(ex, "There was an error importing the user data");
                throw;
            }
        }

        private async Task ProduceObjects(IAsyncEnumerable<IUser> users, ITargetBlock<IUser> target)
        {
            await users.ForEachAsync(t => target.Post(t));
            target.Complete();
        }

        private async Task ConsumeObjects(ImportContext context, SchemaType type, ISourceBlock<IUser> source)
        {
            long userHighestTicks = 0;

            while (await source.OutputAvailableAsync())
            {
                IUser user = source.Receive();

                try
                {
                    if (user.LastUpdated.HasValue)
                    {
                        AsyncHelper.InterlockedMax(ref userHighestTicks, user.LastUpdated.Value.Ticks);
                    }

                    CSEntryChange c = await this.UserToCSEntryChange(context.InDelta, type, user, context).ConfigureAwait(false);

                    if (c != null)
                    {
                        context.ImportItems.Add(c, context.CancellationTokenSource.Token);
                    }
                }
                catch (Exception ex)
                {
                    UserImportProvider.logger.Error(ex);
                    CSEntryChange csentry = CSEntryChange.Create();
                    csentry.DN = user.Id;
                    csentry.ErrorCodeImport = MAImportError.ImportErrorCustomContinueRun;
                    csentry.ErrorDetail = ex.StackTrace;
                    csentry.ErrorName = ex.Message;
                    context.ImportItems.Add(csentry, context.CancellationTokenSource.Token);
                }

                context.CancellationTokenSource.Token.ThrowIfCancellationRequested();
            }

            string wmv;

            if (userHighestTicks <= 0)
            {
                wmv = context.IncomingWatermark["users"].Value;
            }
            else
            {
                wmv = userHighestTicks.ToString();
            }

            context.OutgoingWatermark.Add(new Watermark("users", wmv, "DateTime"));
        }

        private async Task<CSEntryChange> UserToCSEntryChange(bool inDelta, SchemaType schemaType, IUser user, ImportContext context)
        {
            Resource profile = user.GetProperty<Resource>("profile");
            string login = profile.GetProperty<string>("login");
            logger.Trace($"Creating CSEntryChange for {user.Id}/{login}");

            ObjectModificationType modType = this.GetObjectModificationType(user, inDelta, context);

            if (modType == ObjectModificationType.None)
            {
                return null;
            }

            CSEntryChange c = CSEntryChange.Create();
            c.ObjectType = "user";
            c.ObjectModificationType = modType;
            c.AnchorAttributes.Add(AnchorAttribute.Create("id", user.Id));
            c.DN = user.Id;

            if (modType != ObjectModificationType.Delete)
            {
                foreach (SchemaAttribute type in schemaType.Attributes)
                {
                    if (type.Name == "provider.name")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(user.Credentials.Provider.Name, type.DataType)));
                        continue;
                    }
                    else if (type.Name == "provider.type")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(user.Credentials.Provider.Type.Value, type.DataType)));
                        continue;
                    }
                    else if (type.Name == "suspended")
                    {
                        c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, user.Status == UserStatus.Suspended));
                        continue;
                    }
                    else if (type.Name == "enrolledFactors")
                    {

                        List<object> items = new List<object>();
                        foreach (IFactor factor in await user.ListFactors().ToList().ConfigureAwait(false))
                        {
                            items.Add($"{factor.Provider}/{factor.FactorType}");
                        }

                        if (items.Count > 0)
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, items));
                        }

                        continue;
                    }
                    else if (type.Name == "availableFactors")
                    {
                        List<object> items = new List<object>();
                        foreach (IFactor factor in await user.ListSupportedFactors().ToList().ConfigureAwait(false))
                        {
                            items.Add($"{factor.Provider}/{factor.FactorType}");
                        }

                        if (items.Count > 0)
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, items));
                        }

                        continue;
                    }

                    object value = user.GetProperty<object>(type.Name) ?? profile.GetProperty<object>(type.Name);

                    if (value != null)
                    {
                        if (value is IList list)
                        {
                            IList<object> values = new List<object>();

                            foreach (object item in list)
                            {
                                values.Add(TypeConverter.ConvertData(item, type.DataType));
                            }

                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, values));
                        }
                        else
                        {
                            c.AttributeChanges.Add(AttributeChange.CreateAttributeAdd(type.Name, TypeConverter.ConvertData(value, type.DataType)));
                        }
                    }
                }
            }

            return c;
        }

        private IAsyncEnumerable<IUser> GetUserEnumerable(bool inDelta, WatermarkKeyedCollection importState, IOktaClient client, ImportContext context)
        {
            IAsyncEnumerable<IUser> users;

            if (inDelta)
            {
                if (importState == null)
                {
                    throw new WarningNoWatermarkException("No watermark was available to perform a delta import");
                }

                if (!importState.Contains("users"))
                {
                    throw new WarningNoWatermarkException("No watermark was available to perform a delta import for the user object type");
                }

                string value = importState["users"].Value;
                long ticks = long.Parse(value);
                DateTime dt = new DateTime(ticks);

                string filter = $"(lastUpdated gt \"{dt.ToSmartString()}Z\")";

                if (context.ConfigParameters[ConfigParameterNames.UserDeprovisioningAction].Value == "Delete")
                {
                    filter += " and(status eq \"LOCKED_OUT\" or status eq \"RECOVERY\" or status eq \"STAGED\" or status eq \"PROVISIONED\" or status eq \"ACTIVE\" or status eq \"PASSWORD_EXPIRED\" or status eq \"DEPROVISIONED\" or status eq \"SUSPENDED\")";
                }

                users = client.Users.ListUsers(null, null, OktaMAConfigSection.Configuration.UserListPageSize, filter);
            }
            else
            {
                string filter = null;

                if (context.ConfigParameters[ConfigParameterNames.UserDeprovisioningAction].Value == "Delete")
                {
                    filter = "(status eq \"LOCKED_OUT\" or status eq \"RECOVERY\" or status eq \"STAGED\" or status eq \"PROVISIONED\" or status eq \"ACTIVE\" or status eq \"PASSWORD_EXPIRED\" or status eq \"DEPROVISIONED\" or status eq \"SUSPENDED\")";
                }

                users = client.Users.ListUsers(null, null, OktaMAConfigSection.Configuration.UserListPageSize, filter);
            }

            return users;
        }

        private ObjectModificationType GetObjectModificationType(IUser user, bool inDelta, ImportContext context)
        {
            if (!inDelta)
            {
                if (context.ConfigParameters[ConfigParameterNames.UserDeprovisioningAction].Value != "Delete")
                {
                    if ((user.Status?.Value == UserStatus.Deprovisioned ||
                         user.TransitioningToStatus?.Value == UserStatus.Deprovisioned) &&
                        user.TransitioningToStatus?.Value != UserStatus.Provisioned)
                    {

                        logger.Trace($"Discarding {user.Id} as status is deprovisioned");
                        return ObjectModificationType.None;
                    }
                }

                return ObjectModificationType.Add;
            }

            if (context.ConfigParameters[ConfigParameterNames.UserDeprovisioningAction].Value != "Delete")
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

        public bool CanImport(SchemaType type)
        {
            return type.Name == "user";
        }
    }
}