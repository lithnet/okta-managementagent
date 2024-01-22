﻿using System;
using System.Collections;
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
    internal class UserExportProvider : IObjectExportProvider
    {
        private ExportContext context;
        private readonly IOktaClient client;
        private readonly GlobalOptions globalOptions;
        private readonly ILogger<UserExportProvider> logger;

        public UserExportProvider(OktaClientProvider clientProvider, IOptions<GlobalOptions> globalOptions, ILogger<UserExportProvider> logger)
        {
            this.client = clientProvider.GetClient();
            this.globalOptions = globalOptions.Value;
            this.logger = logger;
        }

        public Task<bool> CanExportAsync(CSEntryChange csentry)
        {
            return Task.FromResult(csentry.ObjectType == "user");
        }

        public Task InitializeAsync(ExportContext context)
        {
            this.context = context;
            return Task.CompletedTask;
        }

        public Task<CSEntryChangeResult> PutCSEntryChangeAsync(CSEntryChange csentry)
        {
            return this.PutCSEntryChangeObjectAsync(csentry);
        }

        private Task<CSEntryChangeResult> PutCSEntryChangeObjectAsync(CSEntryChange csentry)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return this.PutCSEntryChangeAddAsync(csentry);

                case ObjectModificationType.Delete:
                    return this.PutCSEntryChangeDeleteAsync(csentry);

                case ObjectModificationType.Update:
                    return this.PutCSEntryChangeUpdateAsync(csentry);

                case ObjectModificationType.Unconfigured:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                default:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeDeleteAsync(CSEntryChange csentry)
        {
            await this.client.Users.DeactivateUserAsync(csentry.DN, cancellationToken: this.context.Token);

            if (this.globalOptions.UserDeprovisioningAction == "Delete")
            {
                await this.client.Users.DeactivateOrDeleteUserAsync(csentry.DN, this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeAddAsync(CSEntryChange csentry)
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
                    this.logger.LogInformation($"Set {change.Name} to {provider.Type ?? "<null>"}");
                }
                else if (change.Name == "provider.name")
                {
                    provider.Name = change.GetValueAdd<string>();
                    this.logger.LogInformation($"Set {change.Name} to {provider.Name ?? "<null>"}");
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
                        this.logger.LogInformation($"Set {change.Name} to {profile[change.Name] ?? "<null>"}");
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

                result = await this.client.Users.CreateUserAsync(options, this.context.Token);
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

                result = await this.client.Users.CreateUserAsync(options, this.context.Token);
            }

            if (this.globalOptions.ActivateNewUsers)
            {
                await this.client.Users.ActivateUserAsync(result.Id, this.globalOptions.SendActivationEmailToNewUsers, this.context.Token);
            }

            if (suspend)
            {
                await result.SuspendAsync(this.context.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeUpdateAsync(CSEntryChange csentry)
        {
            IUser user = null;
            bool partial = true;

            if (csentry.AttributeChanges.Any(t =>
                    t.ModificationType == AttributeModificationType.Delete
                    || t.Name == "suspended"
                    || t.DataType == AttributeType.Reference // this should only need to include MV attributes, but there's an issue where MIM sends an attribute update with a value delete for a single valued ref that it doesn't know about
                ))
            {
                this.logger.LogTrace($"Getting user {csentry.DN} for FULL update");
                user = await this.client.Users.GetUserAsync(csentry.DN, this.context.Token);
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
                        this.logger.LogInformation($"Suspending user {user.Id}");
                        await user.SuspendAsync(this.context.Token);
                    }
                    else if (user.Status == UserStatus.Suspended && !suspend)
                    {
                        this.logger.LogInformation($"Unsuspending user {user.Id}");
                        await user.UnsuspendAsync(this.context.Token);
                    }
                }
                else
                {
                    if (change.IsMultiValued)
                    {
                        IList<object> existingItems = user.Profile[change.Name] as IList<object> ?? new List<object>();
                        IList<object> newList = change.ApplyAttributeChanges((IList)existingItems);
                        user.Profile[change.Name] = newList;

                        this.logger.LogInformation($"{change.ModificationType} {change.Name} -> {newList.Count} items");
                    }
                    else
                    {
                        user.Profile[change.Name] = change.GetValueAdd<object>();
                        this.logger.LogInformation($"{change.ModificationType} {change.Name} -> {user.Profile[change.Name] ?? "<null>"}");
                    }
                }
            }

            if (partial)
            {
                await this.client.PostAsync<User>($"/api/v1/users/{csentry.DN}", user, this.context.Token);
            }
            else
            {
                await this.client.Users.UpdateUserAsync(user, csentry.DN, this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
