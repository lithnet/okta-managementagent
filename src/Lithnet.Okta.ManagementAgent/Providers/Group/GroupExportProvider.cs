using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.Extensions.Logging;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class GroupExportProvider : IObjectExportProvider
    {
        private readonly IOktaClient client;
        private readonly ILogger<GroupExportProvider> logger;

        public GroupExportProvider(OktaClientProvider clientProvider, ILogger<GroupExportProvider> logger)
        {
            this.client = clientProvider.GetClient();
            this.logger = logger;
        }

        public Task InitializeAsync(ExportContext context)
        {
            return Task.CompletedTask;
        }

        public Task<bool> CanExportAsync(CSEntryChange csentry)
        {
            return Task.FromResult(csentry.ObjectType == "group");
        }

        public Task<CSEntryChangeResult> PutCSEntryChangeAsync(CSEntryChange csentry, CancellationToken token)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return this.PutCSEntryChangeAddAsync(csentry, token);

                case ObjectModificationType.Delete:
                    return this.PutCSEntryChangeDeleteAsync(csentry, token);

                case ObjectModificationType.Update:
                    return this.PutCSEntryChangeUpdateAsync(csentry, token);

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeDeleteAsync(CSEntryChange csentry, CancellationToken token)
        {
            await this.client.Groups.DeleteGroupAsync(csentry.DN, token);
            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeAddAsync(CSEntryChange csentry, CancellationToken token)
        {
            CreateGroupOptions options = new CreateGroupOptions();
            IList<string> members = new List<string>();

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "name")
                {
                    options.Name = change.GetValueAdd<string>();
                }
                else if (change.Name == "description")
                {
                    options.Description = change.GetValueAdd<string>();
                }
                else if (change.Name == "member")
                {
                    members = change.GetValueAdds<string>();
                }
            }

            IGroup result = await this.client.Groups.CreateGroupAsync(options, token);

            foreach (string member in members)
            {
                await this.client.Groups.AddUserToGroupAsync(result.Id, member, token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeUpdateAsync(CSEntryChange csentry, CancellationToken token)
        {
            IGroup group = null;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "name")
                {
                    if (group == null)
                    {
                        group = await this.client.Groups.GetGroupAsync(csentry.DN, token);
                    }

                    group.Profile.Name = change.GetValueAdd<string>();
                }
                else if (change.Name == "description")
                {
                    if (group == null)
                    {
                        group = await this.client.Groups.GetGroupAsync(csentry.DN, token);
                    }

                    group.Profile.Description = change.GetValueAdd<string>();
                }
                else if (change.Name == "member")
                {
                    foreach (string add in change.GetValueAdds<string>())
                    {
                        await this.client.Groups.AddUserToGroupAsync(csentry.DN, add, token);
                    }

                    foreach (string delete in change.GetValueDeletes<string>())
                    {
                        await this.client.Groups.RemoveUserFromGroupAsync(csentry.DN, delete, token);
                    }
                }
            }

            if (group != null)
            {
                await this.client.Groups.UpdateGroupAsync(group, csentry.DN, token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
