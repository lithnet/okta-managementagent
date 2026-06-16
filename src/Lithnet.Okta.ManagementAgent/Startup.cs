using System;
using System.IO;
using System.Reflection;
using Lithnet.Ecma2Framework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

namespace Lithnet.Okta.ManagementAgent
{
    public class Startup : IEcmaStartup
    {
        public void Configure(IConfigurationBuilder builder)
        {
            // The hidden advanced overrides (proxy URL, connection limit, page sizes, HTTP debug) were read
            // from the host process's app.config <lithnet-okta-ma> section in the net48 in-process v2. On the
            // v3 net8 worker they come from an appsettings.json that ships next to the worker. The file is
            // optional - when absent, the OktaMAConfigSection defaults apply, matching the v2 unconfigured
            // behaviour. It is loaded from the worker assembly's own directory so the working directory the
            // host launches the worker from does not affect resolution.
            string baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            builder
                .SetBasePath(baseDirectory ?? AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);
        }

        public void SetupServices(IServiceCollection services, IConfigParameters configParameters)
        {
            services.AddSingleton<OktaClientProvider>();

            // Bind the hidden advanced overrides from appsettings.json's "OktaMA" section. BindConfiguration
            // resolves the IConfiguration the framework registered in DI (which includes the appsettings.json
            // source added in Configure above), so IOptions<OktaMAConfigSection> reflects the file or the
            // class defaults when the section is absent.
            services.AddOptions<OktaMAConfigSection>().BindConfiguration(OktaMAConfigSection.SectionName);
            services.AddSingleton<ISchemaProvider, SchemaProvider>();
            services.AddSingleton<ICapabilitiesProvider, CapabilitiesProvider>();

            services.AddSingleton<IObjectImportProvider, UserImportProvider>();
            services.AddSingleton<IObjectImportProvider, GroupImportProvider>();
            services.AddSingleton<IObjectPasswordProvider, UserPasswordProvider>();

            services.AddSingleton<IObjectExportProvider, GroupExportProvider>();
            services.AddSingleton<IObjectExportProvider, UserExportProvider>();

            services.AddLogging(builder =>
            {
                builder.AddEventLog(builder =>
                {
                    builder.SourceName = "Lithnet Okta MA";
                    builder.LogName = "Application";
                    builder.Filter = (x, y) => y >= LogLevel.Warning;
                });
                builder.AddNLog(this.GetNLogConfiguration(configParameters));
            });
        }

        private LoggingConfiguration GetNLogConfiguration(IConfigParameters configParameters)
        {
            LoggingConfiguration logConfiguration = new LoggingConfiguration();

            NLog.LogLevel level = NLog.LogLevel.Info;

            if (configParameters.HasValue(ConfigParameterNames.LogLevelParameterName))
            {
                string value = configParameters.GetString(ConfigParameterNames.LogLevelParameterName);

                if (value != null)
                {
                    level = NLog.LogLevel.FromString(value);
                }
            }

            var logFileName = configParameters.GetString(ConfigParameterNames.LogFileParameterName);

            if (!string.IsNullOrWhiteSpace(logFileName))
            {
                FileTarget fileTarget = new FileTarget();
                logConfiguration.AddTarget("file", fileTarget);
                fileTarget.FileName = logFileName;
                fileTarget.Layout = "${longdate}|[${threadid:padding=4}]|${level:uppercase=true:padding=5}|${message}${exception:format=ToString}";
                fileTarget.ArchiveEvery = FileArchivePeriod.Day;
                fileTarget.ArchiveNumbering = ArchiveNumberingMode.Date;
                fileTarget.MaxArchiveFiles = configParameters.GetInt(ConfigParameterNames.LogDaysParameterName, 7);

                LoggingRule rule2 = new LoggingRule("*", level, fileTarget);
                logConfiguration.LoggingRules.Add(rule2);
            }

            return logConfiguration;
        }
    }
}
