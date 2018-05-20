using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class MAConfigParameters
    {
        private const string ParameterNameLogFileName = "Log file";

        private const string ParameterNameApiKey = "API key";

        private const string ParameterNameTenantUrl = "Tenant URL";

        private const string ParameterNameIncludeBuiltInGroups = "Include built-in groups";

        private const string ParameterNameIncludeAppGroups = "Include app groups";

        private const string ParameterNameUserDeprovisioningAction = "User deprovisioning action";

        private KeyedCollection<string, ConfigParameter> configParameters;

        public MAConfigParameters(KeyedCollection<string, ConfigParameter> configParameters)
        {
            this.configParameters = configParameters;
        }

        public string LogFileName => this.configParameters[ParameterNameLogFileName]?.Value;

        public SecureString ApiKey => this.configParameters[ParameterNameApiKey]?.SecureValue;

        public string TenantUrl => this.configParameters[ParameterNameTenantUrl]?.Value;

        public bool IncludeBuiltInGroups => this.configParameters[ParameterNameIncludeBuiltInGroups]?.Value == "1";

        public bool IncludeAppGroups => this.configParameters[ParameterNameIncludeAppGroups]?.Value == "1";

        public string DeprovisioningAction => this.configParameters[ParameterNameUserDeprovisioningAction]?.Value;

        public static IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(MAConfigParameters.ParameterNameTenantUrl, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(MAConfigParameters.ParameterNameApiKey, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(MAConfigParameters.ParameterNameLogFileName, string.Empty));
                    break;

                case ConfigParameterPage.Global:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(MAConfigParameters.ParameterNameIncludeBuiltInGroups, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(MAConfigParameters.ParameterNameIncludeAppGroups, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateDropDownParameter(MAConfigParameters.ParameterNameUserDeprovisioningAction, new string[] { "Delete", "Deactivate" }, false, "Deactivate"));
                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:
                    break;
            }

            return configParametersDefinitions;
        }

        public static ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return new ParameterValidationResult();
        }
    }
}
