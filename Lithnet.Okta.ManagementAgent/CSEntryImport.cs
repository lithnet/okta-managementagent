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

        public static WatermarkKeyedCollection GetCSEntryChanges(ImportContext context)
        {
            WatermarkKeyedCollection outgoingState = new WatermarkKeyedCollection();
            List<Task> taskList = new List<Task>();

            foreach (SchemaType type in context.Types.Types)
            {
                if (type.Name == "user")
                {
                    taskList.Add(Task.Run(() =>
                    {
                        logger.Info("Starting CSEntryImportUsers");
                        CSEntryImportUsers.GetCSEntryChanges(type, context);
                    }, context.CancellationTokenSource.Token));
                }

                if (type.Name == "group")
                {
                    taskList.Add(Task.Run(() =>
                    {
                        logger.Info("Starting CSEntryImportGroup");
                        CSEntryImportGroups.GetCSEntryChanges(type, context);
                    }, context.CancellationTokenSource.Token));
                }
            }

            Task.WaitAll(taskList.ToArray(), context.CancellationTokenSource.Token);

            return outgoingState;
        }

        public static object GetConnectionContext(MAConfigParameters configParameters)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
