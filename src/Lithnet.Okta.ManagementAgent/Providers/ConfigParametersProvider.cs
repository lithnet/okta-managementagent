using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class ConfigParametersProvider : IConfigParametersProvider
    {
        public IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            List<ConfigParameterDefinition> configParametersDefinitions = new List<ConfigParameterDefinition>();

            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(ConfigParameterNames.TenantUrl, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(ConfigParameterNames.ApiKey, string.Empty));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(ConfigParameterNames.LogFileName, string.Empty));
                    break;

                case ConfigParameterPage.Global:
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.IncludeBuiltInGroups, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.IncludeAppGroups, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateDropDownParameter(ConfigParameterNames.UserDeprovisioningAction, new string[] { "Delete", "Deactivate" }, false, "Deactivate"));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.ActivateNewUsers, false));
                    configParametersDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.SendActivationEmailToNewUsers, false));

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
            return new ParameterValidationResult();
        }
    }
}
