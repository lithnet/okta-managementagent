using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class CSEntryImport
    {
        private static Logger logger = LogManager.GetCurrentClassLogger();

        public static WatermarkKeyedCollection GetCSEntryChanges(bool inDelta, MAConfigParameters configParameters, WatermarkKeyedCollection importState, Schema importTypes, CancellationToken cancellationToken, BlockingCollection<CSEntryChange> importItems, IConnectionContext connectionContext)
        {
            WatermarkKeyedCollection outgoingState = new WatermarkKeyedCollection();
            List<Task> taskList = new List<Task>();

            IOktaClient client = ((OktaConnectionContext) connectionContext).Client;

            foreach (SchemaType type in importTypes.Types)
            {
                if (type.Name == "user")
                {
                    taskList.Add(Task.Run(() =>
                    {
                        logger.Info("Starting CSEntryImportUsers");
                        foreach (Watermark wm in CSEntryImportUsers.GetCSEntryChanges(inDelta, importState, type, cancellationToken, importItems, client))
                        {
                            outgoingState.Add(wm);
                        }
                    }, cancellationToken));
                }

                if (type.Name == "group")
                {
                    taskList.Add(Task.Run(() =>
                    {
                        logger.Info("Starting CSEntryImportGroup");

                        foreach (Watermark wm in CSEntryImportGroups.GetCSEntryChanges(inDelta, configParameters, importState, type, cancellationToken, importItems, client))
                        {
                            outgoingState.Add(wm);
                        }
                    }, cancellationToken));
                }
            }

            Task.WaitAll(taskList.ToArray(), cancellationToken);

            return outgoingState;
        }

        public static IConnectionContext GetConnectionContext(MAConfigParameters configParameters)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
