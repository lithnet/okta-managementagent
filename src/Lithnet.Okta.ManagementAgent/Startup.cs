using Lithnet.Ecma2Framework;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;

namespace Lithnet.Okta.ManagementAgent
{
    internal class Startup : IEcmaStartup
    {
        public void Configure(IConfigurationBuilder builder)
        {
        }

        public void SetupServices(IServiceCollection services, IConfigParameters configParameters)
        {
            services.AddSingleton<OktaClientProvider>();
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
