using System.Diagnostics;
using Microsoft.MetadirectoryServices;
using NLog;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class CSEntryExport
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static CSEntryChangeResult PutCSEntryChange(CSEntryChange csentry, ExportContext context)
        {
            Stopwatch timer = new Stopwatch();

            try
            {
                timer.Start();
                if (csentry.ObjectType == "user")
                {
                    return CSEntryExportUsers.PutCSEntryChange(csentry, context);
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

        public static object GetConnectionContext(MAConfigParameters configParameters)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
