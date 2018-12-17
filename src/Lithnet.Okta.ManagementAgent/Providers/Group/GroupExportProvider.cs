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

        public bool CanExport(CSEntryChange csentry)
        {
            return csentry.ObjectType == "group";
        }

        public CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, ExportContext context)
        {
            return this.PutCSEntryChangeObject(csentry, context);
        }

        public CSEntryChangeResult PutCSEntryChangeObject(CSEntryChange csentry, ExportContext context)
        {
            switch (csentry.ObjectModificationType)
            {
                case ObjectModificationType.Add:
                    return this.PutCSEntryChangeAdd(csentry, context);

                case ObjectModificationType.Delete:
                    return this.PutCSEntryChangeDelete(csentry, context);

                case ObjectModificationType.Update:
                    return this.PutCSEntryChangeUpdate(csentry, context);

                default:
                case ObjectModificationType.None:
                case ObjectModificationType.Replace:
                case ObjectModificationType.Unconfigured:
                    throw new InvalidOperationException($"Unknown or unsupported modification type: {csentry.ObjectModificationType} on object {csentry.DN}");
            }
        }

        private CSEntryChangeResult PutCSEntryChangeDelete(CSEntryChange csentry, ExportContext context)
        {
            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;

            AsyncHelper.RunSync(client.Groups.DeleteGroupAsync(csentry.DN, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeAdd(CSEntryChange csentry, ExportContext context)
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

            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;
            IGroup result = AsyncHelper.RunSync(client.Groups.CreateGroupAsync(options, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);

            foreach (string member in members)
            {
                AsyncHelper.RunSync(client.Groups.AddUserToGroupAsync(result.Id, member, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
            }

            List<AttributeChange> anchorChanges = new List<AttributeChange>();
            anchorChanges.Add(AttributeChange.CreateAttributeAdd("id", result.Id));

            return CSEntryChangeResult.Create(csentry.Identifier, anchorChanges, MAExportError.Success);
        }

        private CSEntryChangeResult PutCSEntryChangeUpdate(CSEntryChange csentry, ExportContext context)
        {
            IOktaClient client = ((OktaConnectionContext)context.ConnectionContext).Client;

            IGroup group = null;

            foreach (AttributeChange change in csentry.AttributeChanges)
            {
                if (change.Name == "name")
                {
                    if (group == null)
                    {
                        group = AsyncHelper.RunSync(client.Groups.GetGroupAsync(csentry.DN, null, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
                    }

                    group.Profile.Name = change.GetValueAdd<string>();
                }
                else if (change.Name == "description")
                {
                    if (group == null)
                    {
                        group = AsyncHelper.RunSync(client.Groups.GetGroupAsync(csentry.DN, null, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
                    }

                    group.Profile.Description = change.GetValueAdd<string>();
                }
                else if (change.Name == "member")
                {
                    foreach (string add in change.GetValueAdds<string>())
                    {
                        AsyncHelper.RunSync(client.Groups.AddUserToGroupAsync(csentry.DN, add, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
                    }

                    foreach (string delete in change.GetValueDeletes<string>())
                    {
                        AsyncHelper.RunSync(client.Groups.RemoveGroupUserAsync(csentry.DN, delete, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
                    }
                }
            }

            if (group != null)
            {
                AsyncHelper.RunSync(client.Groups.UpdateGroupAsync(group, csentry.DN, context.CancellationTokenSource.Token), context.CancellationTokenSource.Token);
            }

            return CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.Success);
        }
    }
}
