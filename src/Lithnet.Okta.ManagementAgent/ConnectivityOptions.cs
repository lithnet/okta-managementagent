using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Lithnet.Ecma2Framework;

namespace Lithnet.Okta.ManagementAgent
{
    [ConnectivityConfiguration]
    internal class ConnectivityOptions
    {
        [LabelParameter("Logging settings")]

        [Required]
        [StringParameter(ConfigParameterNames.LogFileParameterName)]
        public string LogFile { get; set; }

        [Required]
        [DefaultValue("Info")]
        [DropdownParameter(ConfigParameterNames.LogLevelParameterName, false, new string[] { "Trace", "Debug", "Info", "Warn", "Error", "Fatal" })]
        public string LogLevel { get; set; }

        [StringParameter(ConfigParameterNames.LogDaysParameterName)]
        [DefaultValue(30)]
        public int? LogRotationDays { get; set; }

        [DividerParameter]
        [LabelParameter("API settings")]

        [EncryptedStringParameter(ConfigParameterNames.ApiKey)]
        [Required]
        public string ApiKey { get; set; }

        [Required]
        [StringParameter(ConfigParameterNames.TenantUrl)]
        [DefaultValue("https://one.digicert.com")]
        [Url]
        public string TenantUrl { get; set; }

        [DividerParameter]
        [LabelParameter("Advanced settings")]

        [DefaultValue(120)]
        [StringParameter(ConfigParameterNames.HttpClientTimeout)]
        public int? HttpClientTimeout { get; set; }
    }
}
