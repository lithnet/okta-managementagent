using System.Threading;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public class CSEntryExport
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, IOktaClient client, CancellationToken token)
        {
            if (csentry.ObjectType == "user")
            {
                return CSEntryExportUsers.PutCSEntryChange(csentry, client, token);
            }
            else
            {
                throw new NoSuchObjectTypeException();
            }
        }
    }
}
