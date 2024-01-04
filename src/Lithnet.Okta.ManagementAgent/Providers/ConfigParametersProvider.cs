using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class ConfigParametersProvider : IConfigParametersProvider
    {
        public void GetConfigParameters(KeyedCollection<string, ConfigParameter> existingConfigParameters, IList<ConfigParameterDefinition> newDefinitions, ConfigParameterPage page)
        {
            switch (page)
            {
                case ConfigParameterPage.Connectivity:
                    newDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(ConfigParameterNames.TenantUrl, string.Empty));
                    newDefinitions.Add(ConfigParameterDefinition.CreateEncryptedStringParameter(ConfigParameterNames.ApiKey, string.Empty));
                    newDefinitions.Add(ConfigParameterDefinition.CreateStringParameter(ConfigParameterNames.LogFileName, string.Empty));
                    break;

                case ConfigParameterPage.Global:
                    newDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.IncludeBuiltInGroups, false));
                    newDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.IncludeAppGroups, false));
                    newDefinitions.Add(ConfigParameterDefinition.CreateDividerParameter());
                    newDefinitions.Add(ConfigParameterDefinition.CreateDropDownParameter(ConfigParameterNames.UserDeprovisioningAction, new string[] { "Delete", "Deactivate" }, false, "Deactivate"));
                    newDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.ActivateNewUsers, false));
                    newDefinitions.Add(ConfigParameterDefinition.CreateCheckBoxParameter(ConfigParameterNames.SendActivationEmailToNewUsers, false));

                    break;

                case ConfigParameterPage.Partition:
                    break;

                case ConfigParameterPage.RunStep:
                    break;
            }
        }

        public ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return new ParameterValidationResult();
        }
    }
}
