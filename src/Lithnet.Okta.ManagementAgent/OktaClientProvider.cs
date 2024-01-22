using System;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Okta.Sdk;
using Okta.Sdk.Configuration;
using Okta.Sdk.Internal;

namespace Lithnet.Okta.ManagementAgent
{
    internal class OktaClientProvider
    {
        private readonly ILogger<OktaClientProvider> logger;
        private readonly ConnectivityOptions connectivityOptions;
        private OktaClient client;

        public OktaClientProvider(ILogger<OktaClientProvider> clientProvider, IOptions<ConnectivityOptions> connectivityOptions)
        {
            this.logger = clientProvider;
            this.connectivityOptions = connectivityOptions.Value;
        }

        public OktaClient GetClient()
        {
            if (this.client == null)
            {
                ProxyConfiguration proxyConfig = null;

                if (!string.IsNullOrWhiteSpace(OktaMAConfigSection.Configuration.ProxyUrl))
                {
                    proxyConfig = new ProxyConfiguration() { Host = OktaMAConfigSection.Configuration.ProxyUrl };
                    this.logger.LogInformation($"Using proxy host {proxyConfig.Host}");
                }

                this.logger.LogInformation($"Setting up connection to {this.connectivityOptions.TenantUrl}");

                System.Net.ServicePointManager.DefaultConnectionLimit = OktaMAConfigSection.Configuration.ConnectionLimit;

                NLog.Extensions.Logging.NLogLoggerProvider f = new NLog.Extensions.Logging.NLogLoggerProvider();
                ILogger nlogger = f.CreateLogger("ext-logger");

                OktaClientConfiguration oktaConfig = new OktaClientConfiguration
                {
                    OktaDomain = this.connectivityOptions.TenantUrl,
                    Token = this.connectivityOptions.ApiKey,
                    Proxy = proxyConfig,
                    ConnectionTimeout = this.connectivityOptions.HttpClientTimeout,
                    MaxRetries = 8,
                };

                HttpClient httpClient;

                if (OktaMAConfigSection.Configuration.HttpDebugEnabled)
                {
                    this.logger.LogWarning("WARNING: HTTPS Debugging enabled. Service certificate validation is disabled");

                    httpClient = this.CreateDebugHttpClient(nlogger, oktaConfig);
                }
                else
                {
                    httpClient = DefaultHttpClient.Create(oktaConfig.ConnectionTimeout, proxyConfig, nlogger);
                }

                this.client = new OktaClient(oktaConfig, httpClient, nlogger, new DefaultRetryStrategy(oktaConfig.MaxRetries ?? 8, 0, 1));
            }

            return this.client;
        }

        private HttpClient CreateDebugHttpClient(ILogger nlogger, OktaClientConfiguration oktaConfig)
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

            this.logger.LogTrace($"Using timeout of {httpClient.Timeout} second(s) from configuration");

            //// Workaround for https://github.com/dotnet/corefx/issues/11224
            //httpClient.DefaultRequestHeaders.Add("Connection", "close");
            return httpClient;
        }
    }
}
