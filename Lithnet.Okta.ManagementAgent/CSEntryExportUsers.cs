using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class CSEntryExportUsers
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, ExportContext context)
        {

            return CSEntryExportUsers.PutCSEntryChangeObject(csentry, context);
        }

        public static CSEntryChangeResult PutCSEntryChangeObject(CSEntryChange csentry, ExportContext context)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return CSEntryExportUsers.PutCSEntryChangeAdd(csentry, context);

                case ObjectModificationType.Delete:
                    return CSEntryExportUsers.PutCSEntryChangeDelete(csentry, context);

                case ObjectModificationType.Update:
                    return CSEntryExportUsers.PutCSEntryChangeUpdate(csentry, context);

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private static CSEntryChangeResult PutCSEntryChangeDelete(CSEntryChange csentry, ExportContext context)
        {
            IOktaClient client = ((OktaConnectionContext) context.ConnectionContext).Client;

            client.Users.DeactivateUserAsync(csentry.DN, context.CancellationTokenSource.Token).Wait(context.CancellationTokenSource.Token);

            if (context.ConfigParameters.DeprovisioningAction == "Delete")
            {
                client.Users.DeactivateOrDeleteUserAsync(csentry.DN, context.CancellationTokenSource.Token).Wait(context.CancellationTokenSource.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private static CSEntryChangeResult PutCSEntryChangeAdd(CSEntryChange csentry, ExportContext context)
        {
            AuthenticationProvider provider = new AuthenticationProvider();
            provider.Type = AuthenticationProviderType.Okta;

            UserProfile profile = new UserProfile();
            bool suspend = false;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "provider.type")
                {
                    provider.Type = new AuthenticationProviderType(change.GetValueAdd<string>());
                    logger.Info($"Set {change.Name} to {provider.Type ?? "<null>"}");

                }
                else if (change.Name == "provider.name")
                {
                    provider.Name = change.GetValueAdd<string>();
                    logger.Info($"Set {change.Name} to {provider.Name ?? "<null>"}");
                }
                else if (change.Name == "suspended")
                {
                    suspend = change.GetValueAdd<bool>();
                }
                else
                {
                    if (change.IsMultiValued)
                    {
                        profile[change.Name] = change.GetValueAdds<object>();
                    }
                    else
                    {
                        profile[change.Name] = change.GetValueAdd<object>();
                        logger.Info($"Set {change.Name} to {profile[change.Name] ?? "<null>"}");
                    }
                }
            }

            CreateUserWithProviderOptions options = new CreateUserWithProviderOptions()
            {
                Profile = profile,
                ProviderName = provider.Name,
                ProviderType = provider.Type,
                Activate = true
            };

            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;

            IUser result = client.Users.CreateUserAsync(options, context.CancellationTokenSource.Token).Result;

            if (suspend)
            {
                result.SuspendAsync(context.CancellationTokenSource.Token).Wait(context.CancellationTokenSource.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private static CSEntryChangeResult PutCSEntryChangeUpdate(CSEntryChange csentry, ExportContext context)
        {
            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;

            IUser user = client.Users.GetUserAsync(csentry.DN, context.CancellationTokenSource.Token).Result;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "suspended")
                {
                    bool suspend;

                    if (change.ModificationType == AttributeModificationType.Delete)
                    {
                        suspend = false;
                    }
                    else
                    {
                        suspend = change.GetValueAdd<bool>();
                    }

                    if (user.Status == UserStatus.Active && suspend)
                    {
                        logger.Info($"Suspending user {user.Id}");
                        user.SuspendAsync(context.CancellationTokenSource.Token).Wait(context.CancellationTokenSource.Token);
                    }
                    else if (user.Status == UserStatus.Suspended && !suspend)
                    {
                        user.UnsuspendAsync(context.CancellationTokenSource.Token).Wait(context.CancellationTokenSource.Token);
                        logger.Info($"Unsuspending user {user.Id}");
                    }
                }
                else
                {

                    if (change.IsMultiValued)
                    {
                        IList<object> adds = change.GetValueAdds<object>();
                        IList<object> deletes = change.GetValueDeletes<object>();
                        IList<object> existingItems =  user.Profile[change.Name] as IList<object> ?? new List<object>();
                        List<object> newList = new List<object>();

                        newList.AddRange(existingItems.Except(deletes));
                        newList.AddRange(adds);

                        user.Profile[change.Name] = newList;
                    }
                    else
                    {
                        user.Profile[change.Name] = change.GetValueAdd<object>();
                        logger.Info($"Set {change.Name} to {user.Profile[change.Name] ?? "<null>"}");
                    }

                    user.Profile[change.Name] = change.ValueChanges.FirstOrDefault(t => t.ModificationType == ValueModificationType.Add)?.Value;
                    logger.Info($"Set {change.Name} to {user.Profile[change.Name] ?? "<null>"}");
                }
            }

            IUser result = client.Users.UpdateUserAsync(user, csentry.DN, context.CancellationTokenSource.Token).Result;

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
