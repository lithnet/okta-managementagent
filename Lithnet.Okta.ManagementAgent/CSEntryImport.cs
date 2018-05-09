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
    public class CSEntryImport
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static WatermarkKeyedCollection GetCSEntryChanges(bool inDelta, WatermarkKeyedCollection importState, Schema importTypes, CancellationToken cancellationToken, BlockingCollection<CSEntryChange> importItems, IOktaClient client)
        {
            WatermarkKeyedCollection outgoingState = new WatermarkKeyedCollection();

            foreach (SchemaType type in importTypes.Types)
            {
                if (type.Name == "user")
                {
                    foreach (Watermark wm in CSEntryImportUsers.GetCSEntryChanges(inDelta, importState, type, cancellationToken, importItems, client))
                    {
                        outgoingState.Add(wm);
                    }
                }
            }

            return outgoingState;
        }
    }
}
