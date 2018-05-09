using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using Microsoft.MetadirectoryServices.DetachedObjectModel;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public class CSEntryExportUsers
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {

            return CSEntryExportUsers.PutCSEntryChangeObject(csentry, client, token);
        }

        public static CSEntryChangeResult PutCSEntryChangeObject(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return CSEntryExportUsers.PutCSEntryChangeAdd(csentry, client, token);

                case ObjectModificationType.Delete:
                    return CSEntryExportUsers.PutCSEntryChangeDelete(csentry, client, token);

                case ObjectModificationType.Update:
                    return CSEntryExportUsers.PutCSEntryChangeUpdate(csentry, client, token);

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private static CSEntryChangeResult PutCSEntryChangeDelete(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {
            client.Users.DeactivateOrDeleteUserAsync(csentry.DN, token);
            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private static CSEntryChangeResult PutCSEntryChangeAdd(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {
            AuthenticationProvider provider = new AuthenticationProvider();
            provider.Type = AuthenticationProviderType.Okta;

            UserProfile profile = new UserProfile();

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
                else
                {
                    profile[change.Name] = change.GetValueAdd<string>();
                    logger.Info($"Set {change.Name} to {profile[change.Name] ?? "<null>"}");
                }
            }

            //User u = new User();
            //u.Profile = profile;
            //u.Credentials.Provider = provider;

            CreateUserWithProviderOptions options = new CreateUserWithProviderOptions()
            {
                Profile = profile,
                ProviderName = provider.Name,
                ProviderType = provider.Type,
                Activate = true
            };

            IUser result = client.Users.CreateUserAsync(options, token).Result;
            //IUser result = client.Users.CreateUserAsync(user, false, false, token).Result;

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private static CSEntryChangeResult PutCSEntryChangeUpdate(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {
            IUser user = client.Users.GetUserAsync(csentry.DN, token).Result;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                user.Profile[change.Name] = change.ValueChanges.FirstOrDefault(t => t.ModificationType == ValueModificationType.Add)?.Value;
                logger.Info($"Set {change.Name} to {user.Profile[change.Name] ?? "<null>"}");
            }

            IUser result = client.Users.UpdateUserAsync(user, csentry.DN, token).Result;

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
