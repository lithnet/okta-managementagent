using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using NLog;
using NLog.Config;
using NLog.Targets;
using Newtonsoft.Json;

namespace Lithnet.Okta.ManagementAgent
{
    public class Ecma2 :
        IMAExtensible2CallExport,
        IMAExtensible2CallImport,
        IMAExtensible2GetSchema,
        IMAExtensible2GetCapabilities,
        IMAExtensible2GetParameters,
        IMAExtensible2Password
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private ImportContext importContext;

        private ExportContext exportContext;

        private PasswordContext passwordContext;

        private Task producerTask;

        private int PageSize { get; set; }

        private int Batch { get; set; }

        public int ImportDefaultPageSize => 100;

        public int ImportMaxPageSize => 9999;

        public int ExportDefaultPageSize => 100;

        public int ExportMaxPageSize => 9999;

        public MACapabilities Capabilities => new MACapabilities
        {
            ConcurrentOperation = true,
            DeltaImport = true,
            DistinguishedNameStyle = MADistinguishedNameStyle.Generic,
            Normalizations = MANormalizations.None,
            IsDNAsAnchor = false,
            SupportHierarchy = false,
            SupportImport = true,
            SupportPartitions = false,
            SupportPassword = true,
            ExportType = MAExportType.MultivaluedReferenceAttributeUpdate,
            ObjectConfirmation = MAObjectConfirmation.Normal,
            ObjectRename = false,
            SupportExport = true
        };

        // *** Import ***

        public OpenImportConnectionResults OpenImportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);

            this.SetupLogger(maConfigParameters);

            this.importContext = new ImportContext()
            {
                CancellationTokenSource = new CancellationTokenSource(),
                InDelta = importRunStep.ImportType == OperationType.Delta,
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

                this.PageSize = importRunStep.PageSize;
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
                    logger.Info("CSEntryChange production speed: {0} obj/sec", (int)(this.importContext.ImportedItemCount / this.importContext.ProducerDuration.TotalSeconds));
                }

                logger.Info("Import duration: {0}", this.importContext.Timer.Elapsed);
                logger.Info("Import speed: {0} obj/sec", (int)(this.importContext.ImportedItemCount / this.importContext.Timer.Elapsed.TotalSeconds));
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

            this.producerTask = new Task(() =>
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

            this.producerTask.Start();
        }

        private GetImportEntriesResults ConsumePageFromProducer()
        {
            int count = 0;
            bool mayHaveMore = false;
            GetImportEntriesResults results = new GetImportEntriesResults { CSEntries = new List<CSEntryChange>() };

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

                if (count >= this.PageSize)
                {
                    mayHaveMore = true;
                    break;
                }
            }

            if (this.producerTask?.IsFaulted == true)
            {
                throw new TerminateRunException("The producer thread encountered an exception", this.producerTask.Exception);
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

        // *** Schema ***

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            try
            {
                MAConfigParameters maconfigParameters = new MAConfigParameters(configParameters);
                this.SetupLogger(maconfigParameters);

                SchemaContext context = new SchemaContext()
                {
                    ConfigParameters = maconfigParameters,
                    ConnectionContext = MASchema.GetConnectionContext(maconfigParameters)
                };

                return MASchema.GetMmsSchema(context);
            }
            catch (Exception ex)
            {
                logger.Error(ex.UnwrapIfSingleAggregateException(), "Could not retrieve schema");
                throw;
            }
        }

        public IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return MAConfigParameters.GetConfigParameters(configParameters, page);
        }

        public ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return MAConfigParameters.ValidateConfigParameters(configParameters, page);
        }

        // *** Export ***

        public void OpenExportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);

            this.SetupLogger(maConfigParameters);

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
                logger.Info("Speed: {0} obj/sec", (int)(this.exportContext.ExportedItemCount / this.exportContext.Timer.Elapsed.TotalSeconds));
                logger.Info("Average: {0} sec/obj", this.exportContext.Timer.Elapsed.TotalSeconds / this.exportContext.ExportedItemCount);
            }
        }

        public void OpenPasswordConnection(KeyedCollection<string, ConfigParameter> configParameters, Partition partition)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);
            this.SetupLogger(maConfigParameters);
            this.passwordContext = new PasswordContext()
            {
                ConnectionContext = CSEntryPassword.GetConnectionContext(maConfigParameters),
                ConfigParameters = maConfigParameters
            };
        }

        public void ClosePasswordConnection()
        {
        }

        public ConnectionSecurityLevel GetConnectionSecurityLevel()
        {
            return ConnectionSecurityLevel.Secure;
        }

        public void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options)
        {
            try
            {
                logger.Trace($"Setting password for: {csentry.DN}");
                CSEntryPassword.SetPassword(csentry, newPassword, options, this.passwordContext);
                logger.Info($"Successfully set password for: {csentry.DN}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error setting password for {csentry.DN}");
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }

        public void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            try
            {
                logger.Info($"Changing password for: {csentry.DN}");
                CSEntryPassword.ChangePassword(csentry, oldPassword, newPassword, this.passwordContext);
                logger.Info($"Successfully changed password for: {csentry.DN}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error changing password for {csentry.DN}");
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }

        private void SetupLogger(MAConfigParameters configParameters)
        {
            LoggingConfiguration config = new LoggingConfiguration();

            OutputDebugStringTarget traceTarget = new OutputDebugStringTarget();
            config.AddTarget("trace", traceTarget);
            traceTarget.Layout = @"${longdate}|[${threadid}]|${level:uppercase=true:padding=5}|${message}${exception:format=ToString}";

            LoggingRule rule1 = new LoggingRule("*", LogLevel.Trace, traceTarget);
            config.LoggingRules.Add(rule1);

            if (!string.IsNullOrWhiteSpace(configParameters.LogFileName))
            {
                FileTarget fileTarget = new FileTarget();
                config.AddTarget("file", fileTarget);
                fileTarget.FileName = configParameters.LogFileName;
                fileTarget.Layout = "${longdate}|[${threadid}]|${level:uppercase=true:padding=5}|${message}${exception:format=ToString}";
                LoggingRule rule2 = new LoggingRule("*", LogLevel.Trace, fileTarget);
                config.LoggingRules.Add(rule2);
            }

            LogManager.Configuration = config;
        }

    }
}