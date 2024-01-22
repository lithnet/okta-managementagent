using System;
using System.Collections.Generic;
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
        private ExportContext context;
        private readonly IOktaClient client;
        private readonly ILogger<GroupExportProvider> logger;

        public GroupExportProvider(OktaClientProvider clientProvider, ILogger<GroupExportProvider> logger)
        {
            this.client = clientProvider.GetClient();
            this.logger = logger;
        }

        public Task InitializeAsync(ExportContext context)
        {
            this.context = context;
            return Task.CompletedTask;
        }

        public Task<bool> CanExportAsync(CSEntryChange csentry)
        {
            return Task.FromResult(csentry.ObjectType == "group");
        }

        public Task<CSEntryChangeResult> PutCSEntryChangeAsync(CSEntryChange csentry)
        {
            return this.PutCSEntryChangeObjectAsync(csentry);
        }

        public Task<CSEntryChangeResult> PutCSEntryChangeObjectAsync(CSEntryChange csentry)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return this.PutCSEntryChangeAddAsync(csentry);

                case ObjectModificationType.Delete:
                    return this.PutCSEntryChangeDeleteAsync(csentry);

                case ObjectModificationType.Update:
                    return this.PutCSEntryChangeUpdateAsync(csentry);

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeDeleteAsync(CSEntryChange csentry)
        {
            await this.client.Groups.DeleteGroupAsync(csentry.DN, this.context.Token);
            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeAddAsync(CSEntryChange csentry)
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

            IGroup result = await this.client.Groups.CreateGroupAsync(options, this.context.Token);

            foreach (string member in members)
            {
                await this.client.Groups.AddUserToGroupAsync(result.Id, member, this.context.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private async Task<CSEntryChangeResult> PutCSEntryChangeUpdateAsync(CSEntryChange csentry)
        {
            IGroup group = null;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "name")
                {
                    if (group == null)
                    {
                        group = await this.client.Groups.GetGroupAsync(csentry.DN, this.context.Token);
                    }

                    group.Profile.Name = change.GetValueAdd<string>();
                }
                else if (change.Name == "description")
                {
                    if (group == null)
                    {
                        group = await this.client.Groups.GetGroupAsync(csentry.DN, this.context.Token);
                    }

                    group.Profile.Description = change.GetValueAdd<string>();
                }
                else if (change.Name == "member")
                {
                    foreach (string add in change.GetValueAdds<string>())
                    {
                        await this.client.Groups.AddUserToGroupAsync(csentry.DN, add, this.context.Token);
                    }

                    foreach (string delete in change.GetValueDeletes<string>())
                    {
                        await this.client.Groups.RemoveUserFromGroupAsync(csentry.DN, delete, this.context.Token);
                    }
                }
            }

            if (group != null)
            {
                await this.client.Groups.UpdateGroupAsync(group, csentry.DN, this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
