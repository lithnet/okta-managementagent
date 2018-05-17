using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public static class CSEntryExport
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal static CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, IOktaClient client, KeyedCollection<string, ConfigParameter> configParameters, CancellationToken token)
        {
            Stopwatch timer = new Stopwatch();

            try
            {
                timer.Start();
                if (csentry.ObjectType == "user")
                {
                    return CSEntryExportUsers.PutCSEntryChange(csentry, client, configParameters, token);
                }
                else
                {
                    throw new NoSuchObjectTypeException();
                }
            }
            finally
            {
                timer?.Stop();
                logger.Trace($"Export of {csentry.DN} took {timer.Elapsed}");
            }
        }
    }
}
