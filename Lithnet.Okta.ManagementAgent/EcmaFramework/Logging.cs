using NLog;
using NLog.Config;
using NLog.Targets;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class Logging
    {
        public static  void SetupLogger(MAConfigParameters configParameters)
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
            LogManager.ReconfigExistingLoggers();
        }
    }
}
