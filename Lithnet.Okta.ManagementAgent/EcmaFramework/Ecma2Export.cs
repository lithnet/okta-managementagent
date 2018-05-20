using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using NLog;

namespace Lithnet.Okta.ManagementAgent
{
    public class Ecma2Export :
        IMAExtensible2CallExport
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private ExportContext exportContext;

        public int ExportDefaultPageSize => 100;

        public int ExportMaxPageSize => 9999;

        public void OpenExportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);

            Logging.SetupLogger(maConfigParameters);

            this.exportContext = new ExportContext()
            {
                CancellationTokenSource = new CancellationTokenSource(),
                ConfigParameters = maConfigParameters
            };

            try
            {
                logger.Info("Starting export");
                this.exportContext.ConnectionContext = CSEntryExport.GetConnectionContext(maConfigParameters);
                this.exportContext.Timer.Start();
            }
            catch (Exception ex)
            {
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            PutExportEntriesResults results = new PutExportEntriesResults();

            ParallelOptions po = new ParallelOptions
            {
                MaxDegreeOfParallelism = MAConfigSection.Configuration.ExportThreads,
                CancellationToken = this.exportContext.CancellationTokenSource.Token
            };

            Parallel.ForEach(csentries, po, (csentry) =>
            {
                Interlocked.Increment(ref this.exportContext.ExportedItemCount);
                logger.Info("Performing export for " + csentry.DN);
                try
                {
                    CSEntryChangeResult result = CSEntryExport.PutCSEntryChange(csentry, this.exportContext);
                    lock (results)
                    {
                        results.CSEntryChangeResults.Add(result);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex.UnwrapIfSingleAggregateException());
                    lock (results)
                    {
                        results.CSEntryChangeResults.Add(CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.ExportErrorCustomContinueRun, ex.UnwrapIfSingleAggregateException().Message, ex.UnwrapIfSingleAggregateException().ToString()));
                    }
                }
            });

            return results;
        }

        public void CloseExportConnection(CloseExportConnectionRunStep exportRunStep)
        {
            logger.Info("Closing export connection: {0}", exportRunStep.Reason);

            if (this.exportContext == null)
            {
                logger.Trace("No export context detected");
                return;
            }

            this.exportContext.Timer.Stop();

            if (exportRunStep.Reason != CloseReason.Normal)
            {
                if (this.exportContext.CancellationTokenSource != null)
                {
                    logger.Info("Cancellation request received");
                    this.exportContext.CancellationTokenSource.Cancel();
                    this.exportContext.CancellationTokenSource.Token.WaitHandle.WaitOne();
                    logger.Info("Cancellation completed");
                }
            }

            logger.Info("Export operation complete");
            logger.Info("Exported {0} objects", this.exportContext.ExportedItemCount);
            logger.Info("Export duration: {0}", this.exportContext.Timer.Elapsed);
            if (this.exportContext.ExportedItemCount > 0 && this.exportContext.Timer.Elapsed.TotalSeconds > 0)
            {
                logger.Info("Speed: {0} obj/sec", (int) (this.exportContext.ExportedItemCount / this.exportContext.Timer.Elapsed.TotalSeconds));
                logger.Info("Average: {0} sec/obj", this.exportContext.Timer.Elapsed.TotalSeconds / this.exportContext.ExportedItemCount);
            }
        }
    }
}