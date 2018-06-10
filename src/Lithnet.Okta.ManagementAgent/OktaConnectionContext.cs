using System;
using System.Collections.ObjectModel;
using System.Net.Http;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using NLog;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using Okta.Sdk.Internal;
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

            System.Net.ServicePointManager.DefaultConnectionLimit = OktaMAConfigSection.Configuration.ConnectionLimit;
            GlobalSettings.ExportThreadCount = OktaMAConfigSection.Configuration.ExportThreads;

            NLog.Extensions.Logging.NLogLoggerProvider f = new NLog.Extensions.Logging.NLogLoggerProvider();
            ILogger nlogger = f.CreateLogger("ext-logger");

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

                HttpClient httpClient = OktaConnectionContext.CreateDebugHttpClient(nlogger, oktaConfig);

                return new OktaConnectionContext()
                {
                    Client = new OktaClient(oktaConfig, httpClient, nlogger),
                };
            }

            return new OktaConnectionContext()
            {
                Client = new OktaClient(oktaConfig, nlogger),
            };
        }

        private static HttpClient CreateDebugHttpClient(ILogger nlogger, OktaClientConfiguration oktaConfig)
        {
            HttpClientHandler handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (httpRequestMessage, x509Certificate2, x509Chain, sslPolicyErrors) => true,
            };

            if (oktaConfig.Proxy != null)
            {
                handler.Proxy = new DefaultProxy(oktaConfig.Proxy, nlogger);
            }

            HttpClient httpClient = new HttpClient(handler, true)
            {
                Timeout = TimeSpan.FromSeconds(oktaConfig.ConnectionTimeout ?? OktaClientConfiguration.DefaultConnectionTimeout),
            };

            OktaConnectionContext.logger.Trace($"Using timeout of {httpClient.Timeout} second(s) from configuration");

            // Workaround for https://github.com/dotnet/corefx/issues/11224
            httpClient.DefaultRequestHeaders.Add("Connection", "close");
            return httpClient;
        }
    }
}
