using System.Collections.ObjectModel;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Lithnet.Okta.ManagementAgent
{
    public class OktaConnectionContext
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public IOktaClient Client { get; private set; }

        internal static OktaConnectionContext GetConnectionContext(KeyedCollection<string, ConfigParameter> configParameters)
        {
            ProxyConfiguration proxyConfig = null;

            if (!string.IsNullOrWhiteSpace(OktaMAConfigSection.Configuration.ProxyUrl))
            {
                proxyConfig = new ProxyConfiguration() { Host = OktaMAConfigSection.Configuration.ProxyUrl };
                logger.Info($"Using proxy host {proxyConfig.Host}");
            }

            logger.Info($"Setting up connection to {configParameters[ConfigParameterNames.TenantUrl].Value}");

            OktaClientConfiguration oktaConfig = new OktaClientConfiguration
            {
                OrgUrl = configParameters[ConfigParameterNames.TenantUrl].Value,
                Token = configParameters[ConfigParameterNames.ApiKey].SecureValue.ConvertToUnsecureString(),
                Proxy = proxyConfig,
                ConnectionTimeout = OktaMAConfigSection.Configuration.HttpClientTimeout
            };

            if (OktaMAConfigSection.Configuration.HttpDebugEnabled)
            {
                logger.Warn("WARNING: HTTPS Debugging enabled. Service certificate validation is disabled");
                oktaConfig.DisableServerCertificateValidation = true;
            }

            GlobalSettings.ExportThreadCount = OktaMAConfigSection.Configuration.ExportThreads;

            NLog.Extensions.Logging.NLogLoggerProvider f = new NLog.Extensions.Logging.NLogLoggerProvider();
            ILogger nlogger = f.CreateLogger("ext-logger");

            return new OktaConnectionContext()
            {
                Client = new OktaClient(oktaConfig, nlogger),
            };
        }
    }
}
