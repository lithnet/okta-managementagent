using System;
using System.Collections.Generic;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class GroupExportProvider : IObjectExportProvider
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        private IExportContext context;
        private IOktaClient client;

        public void Initialize(IExportContext context)
        {
            this.context = context;
            this.client = ((OktaConnectionContext)context.ConnectionContext).Client;
        }

        public bool CanExport(CSEntryChange csentry)
        {
            return csentry.ObjectType == "group";
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

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private CSEntryChangeResult PutCSEntryChangeDelete(CSEntryChange csentry)
        {
            AsyncHelper.RunSync(this.client.Groups.DeleteGroupAsync(csentry.DN, this.context.Token), this.context.Token);

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeAdd(CSEntryChange csentry)
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
            
            IGroup result = AsyncHelper.RunSync(this.client.Groups.CreateGroupAsync(options, this.context.Token), this.context.Token);

            foreach (string member in members)
            {
                AsyncHelper.RunSync(this.client.Groups.AddUserToGroupAsync(result.Id, member, this.context.Token), this.context.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeUpdate(CSEntryChange csentry)
        {
            IGroup group = null;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "name")
                {
                    if (group == null)
                    {
                        group = AsyncHelper.RunSync(this.client.Groups.GetGroupAsync(csentry.DN, null, this.context.Token), this.context.Token);
                    }

                    group.Profile.Name = change.GetValueAdd<string>();
                }
                else if (change.Name == "description")
                {
                    if (group == null)
                    {
                        group = AsyncHelper.RunSync(this.client.Groups.GetGroupAsync(csentry.DN, null, this.context.Token), this.context.Token);
                    }

                    group.Profile.Description = change.GetValueAdd<string>();
                }
                else if (change.Name == "member")
                {
                    foreach (string add in change.GetValueAdds<string>())
                    {
                        AsyncHelper.RunSync(this.client.Groups.AddUserToGroupAsync(csentry.DN, add, this.context.Token), this.context.Token);
                    }

                    foreach (string delete in change.GetValueDeletes<string>())
                    {
                        AsyncHelper.RunSync(this.client.Groups.RemoveGroupUserAsync(csentry.DN, delete, this.context.Token), this.context.Token);
                    }
                }
            }

            if (group != null)
            {
                AsyncHelper.RunSync(this.client.Groups.UpdateGroupAsync(group, csentry.DN, this.context.Token), this.context.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
