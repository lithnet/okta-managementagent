using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NLog;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Lithnet.Okta.ManagementAgent
{
    public class OktaConnectionContext : IConnectionContext
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        public IOktaClient Client { get; private set; }

        internal static OktaConnectionContext GetConnectionContext(MAConfigParameters configParameters)
        {
            ProxyConfiguration proxyConfig = null;

            if (!string.IsNullOrWhiteSpace(MAConfigSection.Configuration.ProxyUrl))
            {
                proxyConfig = new ProxyConfiguration() { Host = MAConfigSection.Configuration.ProxyUrl };
                logger.Info($"Using proxy host {proxyConfig.Host}");
            }

            logger.Info($"Setting up connection to {configParameters.TenantUrl}");

            OktaClientConfiguration oktaConfig = new OktaClientConfiguration
            {
                OrgUrl = configParameters.TenantUrl,
                Token = configParameters.ApiKey.ConvertToUnsecureString(),
                Proxy = proxyConfig,
                ConnectionTimeout = MAConfigSection.Configuration.HttpClientTimeout
            };

            if (MAConfigSection.Configuration.HttpDebugEnabled)
            {
                logger.Warn("WARNING: HTTPS Debugging enabled. Service certificate validation is disabled");
                oktaConfig.DisableServerCertificateValidation = true;
            }

            NLog.Extensions.Logging.NLogLoggerProvider f = new NLog.Extensions.Logging.NLogLoggerProvider();
            ILogger nlogger = f.CreateLogger("ext-logger");

            return new OktaConnectionContext()
            {
                Client = new OktaClient(oktaConfig, nlogger),
            };
        }
    }
}
