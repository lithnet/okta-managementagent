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

        private IConnectionContext passwordChangeConnectionContext;

        private IConnectionContext exportConnectionContext;

        private BlockingCollection<CSEntryChange> importCSEntries;

        private double productionDurationSeconds;

        private CancellationTokenSource cancellationTokenSource;

        private bool inDelta;

        private int currentRow;

        private readonly Stopwatch timer = new Stopwatch();

        private Task producerTask;

        private WatermarkKeyedCollection importStateToSave;

        private MAConfigParameters currentConfig;

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

        public OpenImportConnectionResults OpenImportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);

            this.SetupLogger(maConfigParameters);

            this.importCSEntries = new BlockingCollection<CSEntryChange>();
            this.inDelta = importRunStep.ImportType == OperationType.Delta;
            this.cancellationTokenSource = new CancellationTokenSource();

            try
            {
                logger.Info("Starting {0} import", this.inDelta ? "delta" : "full");

                IConnectionContext connectionContext = CSEntryImport.GetConnectionContext(maConfigParameters);

                if (!string.IsNullOrEmpty(importRunStep.CustomData))
                {
                    try
                    {
                        this.importStateToSave = JsonConvert.DeserializeObject<WatermarkKeyedCollection>(importRunStep.CustomData);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Could not deserialize watermark");
                    }
                }

                this.PageSize = importRunStep.PageSize;
                this.timer.Restart();

                this.StartCreatingCSEntryChanges(maConfigParameters, types, this.importStateToSave, connectionContext);
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
            return this.ConsumeImportEntriesFromProducer();
        }

        public CloseImportConnectionResults CloseImportConnection(CloseImportConnectionRunStep importRunStep)
        {
            if (this.timer.IsRunning)
            {
                this.timer.Stop();
            }

            logger.Info("Closing import connection: {0}", importRunStep.Reason);

            if (importRunStep.Reason != CloseReason.Normal)
            {
                if (this.cancellationTokenSource != null)
                {
                    logger.Info("Cancellation request received");
                    this.cancellationTokenSource.Cancel();
                    this.cancellationTokenSource.Token.WaitHandle.WaitOne();
                    this.cancellationTokenSource.Dispose();
                    logger.Info("Cancellation completed");
                }
            }

            this.importCSEntries = null;

            logger.Info("Import operation complete");
            logger.Info("Imported {0} objects", this.currentRow);
            logger.Info("Import duration: {0}", this.timer.Elapsed);

            if (this.currentRow > 0 && this.timer.Elapsed.TotalSeconds > 0)
            {
                if (this.productionDurationSeconds > 0)
                {
                    logger.Info("CSEntryChange production speed: {0} obj/sec", (int)(this.currentRow / this.productionDurationSeconds));
                }

                logger.Info("Import speed: {0} obj/sec", (int)(this.currentRow / this.timer.Elapsed.TotalSeconds));
            }

            if (this.importStateToSave?.Any() == true)
            {
                string wm = JsonConvert.SerializeObject(this.importStateToSave);
                logger.Trace($"Watermark: {wm}");
                return new CloseImportConnectionResults(wm);
            }
            else
            {
                return new CloseImportConnectionResults();
            }
        }

        private void StartCreatingCSEntryChanges(MAConfigParameters configParameters, Schema types, WatermarkKeyedCollection incomingImportState, IConnectionContext connectionContext)
        {
            if (this.importCSEntries == null)
            {
                logger.Info("The import entry list was not created");
                return;
            }

            logger.Info("Starting producer thread");

            this.producerTask = new Task(() =>
            {
                try
                {
                    this.importStateToSave = CSEntryImport.GetCSEntryChanges(this.inDelta, configParameters, incomingImportState, types, this.cancellationTokenSource.Token, this.importCSEntries, connectionContext);
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
                    if (this.timer?.IsRunning == true)
                    {
                        this.productionDurationSeconds = this.timer.Elapsed.TotalSeconds;
                    }

                    logger.Info("CSEntryChange production complete");
                    this.importCSEntries.CompleteAdding();
                }
            });

            this.producerTask.Start();
        }

        private GetImportEntriesResults ConsumeImportEntriesFromProducer()
        {
            int count = 0;
            bool mayHaveMore = false;
            GetImportEntriesResults results = new GetImportEntriesResults { CSEntries = new List<CSEntryChange>() };

            if (this.importCSEntries.IsCompleted)
            {
                results.MoreToImport = false;
                return results;
            }

            this.Batch++;
            logger.Trace($"Producing page {this.Batch}");

            while (!this.importCSEntries.IsCompleted || this.cancellationTokenSource.IsCancellationRequested)
            {
                count++;
                CSEntryChange csentry = null;

                try
                {
                    // May be able to change this to Take(this.PageSize);
                    csentry = this.importCSEntries.Take();
                    this.currentRow++;
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
                IConnectionContext connectionContext = MASchema.GetConnectionContext(maconfigParameters);
                return MASchema.GetMmsSchema(connectionContext);
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
            this.cancellationTokenSource = new CancellationTokenSource();

            try
            {
                logger.Info("Starting export");

                this.currentConfig = maConfigParameters;
                this.exportConnectionContext = CSEntryExport.GetConnectionContext(maConfigParameters);
                this.timer.Restart();
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
                CancellationToken = this.cancellationTokenSource.Token
            };

            Parallel.ForEach(csentries, po, (csentry) =>
            {
                Interlocked.Increment(ref this.currentRow);
                logger.Info("Performing export for " + csentry.DN);
                try
                {
                    CSEntryChangeResult result = CSEntryExport.PutCSEntryChange(csentry, this.exportConnectionContext, this.currentConfig, this.cancellationTokenSource.Token);
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
            if (this.timer.IsRunning)
            {
                this.timer.Stop();
            }

            if (exportRunStep.Reason != CloseReason.Normal)
            {
                if (this.cancellationTokenSource != null)
                {
                    logger.Info("Cancellation request received");
                    this.cancellationTokenSource.Cancel();
                    this.cancellationTokenSource.Token.WaitHandle.WaitOne();
                    logger.Info("Cancellation completed");
                }
            }

            this.importCSEntries = null;

            logger.Info("Export operation complete");
            logger.Info("Exported {0} objects", this.currentRow);
            logger.Info("Export duration: {0}", this.timer.Elapsed);
            if (this.currentRow > 0 && this.timer.Elapsed.TotalSeconds > 0)
            {
                logger.Info("Speed: {0} obj/sec", (int)(this.currentRow / this.timer.Elapsed.TotalSeconds));
                logger.Info("Average: {0} sec/obj", this.timer.Elapsed.TotalSeconds / this.currentRow);
            }
        }

        public void OpenPasswordConnection(KeyedCollection<string, ConfigParameter> configParameters, Partition partition)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);
            this.SetupLogger(maConfigParameters);
            this.passwordChangeConnectionContext = CSEntryPassword.GetConnectionContext(maConfigParameters);
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
                CSEntryPassword.SetPassword(csentry, newPassword, options, this.passwordChangeConnectionContext);
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
                CSEntryPassword.ChangePassword(csentry, oldPassword, newPassword, this.passwordChangeConnectionContext);
                logger.Info($"Successfully changed password for: {csentry.DN}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error changing password for {csentry.DN}");
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }
    }
}