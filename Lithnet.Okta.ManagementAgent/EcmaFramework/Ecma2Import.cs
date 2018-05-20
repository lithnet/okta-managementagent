using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using Newtonsoft.Json;
using NLog;

namespace Lithnet.Okta.ManagementAgent
{
    public class Ecma2Import : IMAExtensible2CallImport
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private ImportContext importContext;

        private int Batch { get; set; }

        public int ImportDefaultPageSize => 100;

        public int ImportMaxPageSize => 9999;
       
        public OpenImportConnectionResults OpenImportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);

            Logging.SetupLogger(maConfigParameters);

            this.importContext = new ImportContext()
            {
                CancellationTokenSource = new CancellationTokenSource(),
                RunStep = importRunStep,
                ImportItems = new BlockingCollection<CSEntryChange>(),
                ConfigParameters = maConfigParameters,
                Types = types
            };

            try
            {
                logger.Info("Starting {0} import", this.importContext.InDelta ? "delta" : "full");

                this.importContext.ConnectionContext = CSEntryImport.GetConnectionContext(maConfigParameters);

                if (!string.IsNullOrEmpty(importRunStep.CustomData))
                {
                    try
                    {
                        this.importContext.IncomingWatermark = JsonConvert.DeserializeObject<WatermarkKeyedCollection>(importRunStep.CustomData);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Could not deserialize watermark");
                    }
                }

                this.importContext.Timer.Start();

                this.StartCreatingCSEntryChanges(this.importContext);
            }
            catch (Exception ex)
            {
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }

            return new OpenImportConnectionResults();
        }

        public GetImportEntriesResults GetImportEntries(GetImportEntriesRunStep importRunStep)
        {
            return this.ConsumePageFromProducer();
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            logger.Info("Closing import connection: {0}", importRunStep.Reason);

            if (this.importContext == null)
            {
                logger.Trace("No import context detected");
                return new CloseImportConnectionResults();
            }

            this.importContext.Timer.Stop();

            if (importRunStep.Reason != CloseReason.Normal)
            {
                if (this.importContext.CancellationTokenSource != null)
                {
                    logger.Info("Cancellation request received");
                    this.importContext.CancellationTokenSource.Cancel();
                    this.importContext.CancellationTokenSource.Token.WaitHandle.WaitOne();
                    logger.Info("Cancellation completed");
                }
            }

            logger.Info("Import operation complete");
            logger.Info("Imported {0} objects", this.importContext.ImportedItemCount);

            if (this.importContext.ImportedItemCount > 0 && this.importContext.Timer.Elapsed.TotalSeconds > 0)
            {
                if (this.importContext.ProducerDuration.TotalSeconds > 0)
                {
                    logger.Info("CSEntryChange production duration: {0}", this.importContext.ProducerDuration);
                    logger.Info("CSEntryChange production speed: {0} obj/sec", (int) (this.importContext.ImportedItemCount / this.importContext.ProducerDuration.TotalSeconds));
                }

                logger.Info("Import duration: {0}", this.importContext.Timer.Elapsed);
                logger.Info("Import speed: {0} obj/sec", (int) (this.importContext.ImportedItemCount / this.importContext.Timer.Elapsed.TotalSeconds));
            }

            if (this.importContext.OutgoingWatermark?.Any() == true)
            {
                string wm = JsonConvert.SerializeObject(this.importContext.OutgoingWatermark);
                logger.Trace($"Watermark: {wm}");
                return new CloseImportConnectionResults(wm);
            }
            else
            {
                return new CloseImportConnectionResults();
            }
        }

        private void StartCreatingCSEntryChanges(ImportContext context)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            logger.Info("Starting producer thread");

            context.Producer = new Task(() =>
            {
                try
                {
                    CSEntryImport.GetCSEntryChanges(context);
                }
                catch (OperationCanceledException)
                {
                    logger.Info("Producer thread canceled");
                }
                catch (Exception ex)
                {
                    logger.Info("Producer thread encountered an exception");
                    logger.Error(ex.UnwrapIfSingleAggregateException());
                    throw;
                }
                finally
                {
                    context.ProducerDuration = context.Timer.Elapsed;
                    logger.Info("CSEntryChange production complete");
                    context.ImportItems.CompleteAdding();
                }
            });

            context.Producer.Start();
        }

        private GetImportEntriesResults ConsumePageFromProducer()
        {
            int count = 0;
            bool mayHaveMore = false;
            GetImportEntriesResults results = new GetImportEntriesResults {CSEntries = new List<CSEntryChange>()};

            if (this.importContext.ImportItems.IsCompleted)
            {
                results.MoreToImport = false;
                return results;
            }

            this.Batch++;
            logger.Trace($"Producing page {this.Batch}");

            while (!this.importContext.ImportItems.IsCompleted || this.importContext.CancellationTokenSource.IsCancellationRequested)
            {
                count++;
                CSEntryChange csentry = null;

                try
                {
                    // May be able to change this to Take(this.PageSize);
                    csentry = this.importContext.ImportItems.Take();
                    this.importContext.ImportedItemCount++;
                }
                catch (InvalidOperationException)
                {
                }

                if (csentry != null)
                {
                    results.CSEntries.Add(csentry);
                }

                if (count >= this.importContext.RunStep.PageSize)
                {
                    mayHaveMore = true;
                    break;
                }
            }

            if (this.importContext.Producer?.IsFaulted == true)
            {
                throw new TerminateRunException("The producer thread encountered an exception", this.importContext.Producer.Exception);
            }

            if (mayHaveMore)
            {
                logger.Trace($"Page {this.Batch} complete");
            }
            else
            {
                logger.Info("CSEntryChange consumption complete");
                this.Batch = 0;
            }

            results.MoreToImport = mayHaveMore;
            return results;
        }
    }
}