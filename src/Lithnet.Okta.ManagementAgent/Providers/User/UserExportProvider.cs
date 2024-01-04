using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class UserExportProvider : IObjectExportProvider
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private IExportContext context;
        private IOktaClient client;

        public bool CanExport(CSEntryChange csentry)
        {
            return csentry.ObjectType == "user";
        }

        public void Initialize(IExportContext context)
        {
            this.context = context;
            this.client = ((OktaConnectionContext)this.context.ConnectionContext).Client;
        }

        public CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry)
        {
            return this.PutCSEntryChangeObject(csentry);
        }

        public CSEntryChangeResult PutCSEntryChangeObject(CSEntryChange csentry)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return this.PutCSEntryChangeAdd(csentry);

                case ObjectModificationType.Delete:
                    return this.PutCSEntryChangeDelete(csentry);

                case ObjectModificationType.Update:
                    return this.PutCSEntryChangeUpdate(csentry);

                case ObjectModificationType.Unconfigured:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                default:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private CSEntryChangeResult PutCSEntryChangeDelete(CSEntryChange csentry)
        {
            AsyncHelper.RunSync(this.client.Users.DeactivateUserAsync(csentry.DN, cancellationToken: this.context.Token), this.context.Token);

            if (this.context.ConfigParameters[ConfigParameterNames.UserDeprovisioningAction].Value == "Delete")
            {
                AsyncHelper.RunSync(this.client.Users.DeactivateOrDeleteUserAsync(csentry.DN, this.context.Token), this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeAdd(CSEntryChange csentry)
        {
            AuthenticationProvider provider = new AuthenticationProvider();
            provider.Type = AuthenticationProviderType.Okta;

            UserProfile profile = new UserProfile();
            bool suspend = false;
            string newPassword = null;

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
                else if (change.Name == "export_password")
                {
                    newPassword = change.GetValueAdd<string>();
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

            IUser result;

            if (newPassword != null)
            {
                CreateUserWithPasswordOptions options = new CreateUserWithPasswordOptions()
                {
                    Password = newPassword,
                    Activate = false,
                    Profile = profile
                };

                result = AsyncHelper.RunSync(this.client.Users.CreateUserAsync(options, this.context.Token), this.context.Token);
            }
            else
            {
                CreateUserWithProviderOptions options = new CreateUserWithProviderOptions()
                {
                    Profile = profile,
                    ProviderName = provider.Name,
                    ProviderType = provider.Type,
                    Activate = false
                };

                result = AsyncHelper.RunSync(this.client.Users.CreateUserAsync(options, this.context.Token), this.context.Token);
            }

            if (this.context.ConfigParameters[ConfigParameterNames.ActivateNewUsers].Value == "1")
            {
                bool sendEmail = this.context.ConfigParameters[ConfigParameterNames.SendActivationEmailToNewUsers].Value == "1";
                AsyncHelper.RunSync(this.client.Users.ActivateUserAsync(result.Id, sendEmail, this.context.Token), this.context.Token);
            }

            if (suspend)
            {
                AsyncHelper.RunSync(result.SuspendAsync(this.context.Token), this.context.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeUpdate(CSEntryChange csentry)
        {
            IUser user = null;
            bool partial = true;

            if (csentry.AttributeChanges.Any(t =>
                    t.ModificationType == AttributeModificationType.Delete
                    || t.Name == "suspended"
                    || t.DataType == AttributeType.Reference // this should only need to include MV attributes, but there's an issue where MIM sends an attribute update with a value delete for a single valued ref that it doesn't know about
                ))
            {
                logger.Trace($"Getting user {csentry.DN} for FULL update");
                user = AsyncHelper.RunSync(this.client.Users.GetUserAsync(csentry.DN, this.context.Token), this.context.Token);
                partial = false;
            }
            else
            {
                user = new User();
                user.Profile = new UserProfile();
            }

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
                        AsyncHelper.RunSync(user.SuspendAsync(this.context.Token), this.context.Token);
                    }
                    else if (user.Status == UserStatus.Suspended && !suspend)
                    {
                        logger.Info($"Unsuspending user {user.Id}");
                        AsyncHelper.RunSync(user.UnsuspendAsync(this.context.Token), this.context.Token);
                    }
                }
                else
                {
                    if (change.IsMultiValued)
                    {
                        IList<object> existingItems = user.Profile[change.Name] as IList<object> ?? new List<object>();
                        IList<object> newList = change.ApplyAttributeChanges((IList)existingItems);
                        user.Profile[change.Name] = newList;

                        logger.Info($"{change.ModificationType} {change.Name} -> {newList.Count} items");
                    }
                    else
                    {
                        user.Profile[change.Name] = change.GetValueAdd<object>();
                        logger.Info($"{change.ModificationType} {change.Name} -> {user.Profile[change.Name] ?? "<null>"}");
                    }
                }
            }

            if (partial)
            {
                AsyncHelper.RunSync(this.client.PostAsync<User>($"/api/v1/users/{csentry.DN}", user, this.context.Token), this.context.Token);
            }
            else
            {
                AsyncHelper.RunSync(this.client.Users.UpdateUserAsync(user, csentry.DN, this.context.Token), this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
