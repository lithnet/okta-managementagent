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
using Okta.Sdk;
using NLog;
using NLog.Config;
using NLog.Targets;
using Okta.Sdk.Configuration;
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
        private static Logger logger = LogManager.GetCurrentClassLogger();

        internal const string ParameterNameLogFileName = "Log file";

        internal const string ParameterNameApiKey = "API key";

        internal const string ParameterNameTenantUrl = "Tenant URL";

        internal const string ParameterNameIncludeBuiltInGroups = "Include built-in groups";

        internal const string ParameterNameIncludeAppGroups = "Include app groups";

        private OktaClient client;

        private BlockingCollection<CSEntryChange> importCSEntries;

        private double productionDurationSeconds;

        private CancellationTokenSource cancellationTokenSource;

        private bool inDelta;

        private int currentRow;

        private Stopwatch timer = new Stopwatch();

        private Task producerTask;

        private WatermarkKeyedCollection importState;

        private Schema importTypes;

        private int PageSize { get; set; }

        private int batch { get; set; }

        public int ImportDefaultPageSize => 100;

        public int ImportMaxPageSize => 9999;

        public int ExportDefaultPageSize => 100;

        public int ExportMaxPageSize => 9999;

        public MACapabilities Capabilities
        {
            get
            {
                MACapabilities capabilities = new MACapabilities
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

                return capabilities;
            }
        }

        // *** Import ***

        private void SetupLogger(KeyedCollection<string, ConfigParameter> configParameters)
        {
            LoggingConfiguration config = new LoggingConfiguration();

            OutputDebugStringTarget traceTarget = new OutputDebugStringTarget();
            config.AddTarget("trace", traceTarget);
            traceTarget.Layout = @"${longdate}|${level:uppercase=true:padding=5}|${message}${exception:format=ToString}";

            LoggingRule rule1 = new LoggingRule("*", LogLevel.Trace, traceTarget);
            config.LoggingRules.Add(rule1);

            if (!string.IsNullOrWhiteSpace(configParameters[ParameterNameLogFileName].Value))
            {
                FileTarget fileTarget = new FileTarget();
                config.AddTarget("file", fileTarget);
                fileTarget.FileName = $"{configParameters[ParameterNameLogFileName].Value}";
                fileTarget.Layout = "${longdate}|${level:uppercase=true:padding=5}|${message}${exception:format=ToString}";
                LoggingRule rule2 = new LoggingRule("*", LogLevel.Trace, fileTarget);
                config.LoggingRules.Add(rule2);
            }

            LogManager.Configuration = config;
        }

        public OpenImportConnectionResults OpenImportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenImportConnectionRunStep importRunStep)
        {
            this.SetupLogger(configParameters);

            this.importCSEntries = new BlockingCollection<CSEntryChange>();
            this.inDelta = importRunStep.ImportType == OperationType.Delta;
            this.cancellationTokenSource = new CancellationTokenSource();
            this.importTypes = types;

            try
            {
                logger.Info("Starting {0} import", this.inDelta ? "delta" : "full");

                this.OpenConnection(configParameters);

                if (!string.IsNullOrEmpty(importRunStep.CustomData))
                {
                    try
                    {
                        this.importState = JsonConvert.DeserializeObject<WatermarkKeyedCollection>(importRunStep.CustomData);
                        //this.importState = importRunStep.CustomData.XmlDeserializeFromString<WatermarkKeyedCollection>();
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, "Could not deserialize watermark");
                    }
                }

                this.PageSize = importRunStep.PageSize;
                this.timer.Restart();

                this.StartCreatingCSEntryChanges(configParameters);
            }
            catch (Exception ex)
            {
                logger.Error(ex);
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
            if (this.currentRow > 0 && this.timer.Elapsed.TotalSeconds > 0)
            {
                if (this.productionDurationSeconds > 0)
                {
                    logger.Info("CSEntryChange production speed: {0} obj/sec", (int)(this.currentRow / this.productionDurationSeconds));
                }

                logger.Info("Import speed: {0} obj/sec", (int)(this.currentRow / this.timer.Elapsed.TotalSeconds));
            }

            if (this.importState != null && this.importState.Any())
            {
                string wm = JsonConvert.SerializeObject(this.importState);  //this.importState.XmlSerializeToString();
                logger.Trace($"Watermark: {wm}");
                return new CloseImportConnectionResults(wm);
            }
            else
            {
                return new CloseImportConnectionResults();
            }
        }

        private void StartCreatingCSEntryChanges(KeyedCollection<string, ConfigParameter> configParameters)
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
                    this.importState = CSEntryImport.GetCSEntryChanges(this.inDelta, configParameters, this.importState, this.importTypes, this.cancellationTokenSource.Token, this.importCSEntries, this.client);
                }
                catch (OperationCanceledException)
                {
                    logger.Info("Producer thread canceled");
                }
                catch (Exception ex)
                {
                    logger.Info("Producer thread encountered an exception");
                    logger.Error(ex);
                    throw;
                }
                finally
                {
                    if (this.timer != null && this.timer.IsRunning)
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

            this.batch++;
            logger.Info($"Producing page {this.batch}");

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

            if (this.producerTask != null && this.producerTask.IsFaulted)
            {
                throw new TerminateRunException("The producer thread encountered an exception", this.producerTask.Exception);
            }

            if (mayHaveMore)
            {
                logger.Info("Page complete");
            }
            else
            {
                logger.Info("CSEntryChange consumption complete");
                this.batch = 0;
            }

            results.MoreToImport = mayHaveMore;
            return results;
        }

        // *** Schema ***

        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            this.SetupLogger(configParameters);
            this.OpenConnection(configParameters);

            return MASchema.GetMmsSchema(this.client);
        }



        public IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(Ecma2.ParameterNameTenantUrl, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(Ecma2.ParameterNameApiKey, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(Ecma2.ParameterNameLogFileName, string.Empty));
                    break;

                case ConfigParameterPage.Global:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(Ecma2.ParameterNameIncludeBuiltInGroups, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(Ecma2.ParameterNameIncludeAppGroups, false));
                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:
                    break;
            }

            return configParametersDefinitions;
        }

        public ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            ParameterValidationResult myResults = new ParameterValidationResult();
            return myResults;
        }

        // *** Export ***

        public void OpenExportConnection(KeyedCollection<string, ConfigParameter> configParameters, Schema types, OpenExportConnectionRunStep exportRunStep)
        {
            this.SetupLogger(configParameters);

            try
            {
                logger.Info("Starting export");

                this.OpenConnection(configParameters);
                this.timer = new Stopwatch();
                this.timer.Start();
            }
            catch (Exception ex)
            {
                logger.Error(ex);
                throw;
            }
        }

        public PutExportEntriesResults PutExportEntries(IList<CSEntryChange> csentries)
        {
            PutExportEntriesResults results = new PutExportEntriesResults();

            ParallelOptions po = new ParallelOptions { MaxDegreeOfParallelism = MAConfigSection.Configuration.ExportThreads };

            Parallel.ForEach(csentries, po, (csentry) =>
            {

                Interlocked.Increment(ref this.currentRow);
                logger.Info("Performing export for " + csentry.DN);
                try
                {
                    CSEntryChangeResult result = CSEntryExport.PutCSEntryChange(csentry, this.client, new CancellationToken());
                    results.CSEntryChangeResults.Add(result);
                }
                catch (Exception ex)
                {
                    logger.Error(ex.GetExceptionContent());
                    results.CSEntryChangeResults.Add(CSEntryChangeResult.Create(csentry.Identifier, null, MAExportError.ExportErrorCustomContinueRun, ex.GetExceptionMessage(), ex.GetExceptionContent()));
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

            this.importCSEntries = null;

            logger.Info("Export operation complete");
            logger.Info("Exported {0} objects", this.currentRow);
            if (this.currentRow > 0 && this.timer.Elapsed.TotalSeconds > 0)
            {
                logger.Info("Speed: {0} obj/sec", (int)(this.currentRow / this.timer.Elapsed.TotalSeconds));
            }
        }

        // ** Helper functions ***

        private void OpenConnection(KeyedCollection<string, ConfigParameter> configParameters)
        {
            this.timer.Start();

            ProxyConfiguration proxyConfig = null;

            if (MAConfigSection.Configuration.ProxyUrl != null)
            {
                proxyConfig = new ProxyConfiguration() { Host = MAConfigSection.Configuration.ProxyUrl };
            }

            logger.Info($"Setting up connection to {configParameters[ParameterNameTenantUrl].Value}");

            OktaClientConfiguration oktaConfig = new OktaClientConfiguration
            {
                OrgUrl = configParameters[ParameterNameTenantUrl].Value,
                Token = configParameters[ParameterNameApiKey].SecureValue.ConvertToUnsecureString(),
                Proxy = proxyConfig,
            };

            if (MAConfigSection.Configuration.HttpDebugEnabled)
            {
                logger.Warn("WARNING: HTTPS Debugging enabled. Service certificate validation is disabled");
                oktaConfig.DisableServerCertificateValidation = true;
            }

            NLog.Extensions.Logging.NLogLoggerProvider f = new NLog.Extensions.Logging.NLogLoggerProvider();
            var nlogger = f.CreateLogger("ext-logger");
            this.client = new OktaClient(oktaConfig, nlogger);
            this.timer.Stop();

            logger.Info("Opened connection: {0}", this.timer.Elapsed.ToString());
        }

        public void OpenPasswordConnection(KeyedCollection<string, ConfigParameter> configParameters, Partition partition)
        {
            this.SetupLogger(configParameters);
            this.OpenConnection(configParameters);
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
                logger.Info($"Set password for: {csentry.DN}");

                User u = new User
                {
                    Credentials = new UserCredentials()
                    {
                        Password = new PasswordCredential()
                        {
                            Value = newPassword.ConvertToUnsecureString()
                        }
                    }
                };

                this.client.Users.UpdateUserAsync(u, csentry.DN.ToString()).Wait();
            }
            catch (Exception ex)
            {
                logger.Error($"Error setting password for {csentry.DN}");
                logger.Error(ex.GetExceptionContent());
                throw;
            }
        }

        public void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            try
            {
                logger.Info($"Change password for: {csentry.DN}");
                this.client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                    new ChangePasswordOptions()
                    {
                        NewPassword = newPassword.ConvertToUnsecureString(),
                        CurrentPassword = oldPassword.ConvertToUnsecureString()
                    }).Wait();
            }
            catch (Exception ex)
            {
                logger.Error($"Error changing password for {csentry.DN}");
                logger.Error(ex.GetExceptionContent());
                throw;
            }
        }
    }
}