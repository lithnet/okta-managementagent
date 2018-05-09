using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.MetadirectoryServices;
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
