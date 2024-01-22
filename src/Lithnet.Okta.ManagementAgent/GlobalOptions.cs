using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Lithnet.Ecma2Framework;

namespace Lithnet.Okta.ManagementAgent
{
    [GlobalConfiguration]
    internal class GlobalOptions
    {
        [LabelParameter("Group options")]
        [CheckboxParameter(ConfigParameterNames.IncludeBuiltInGroups)]
        public bool IncludeBuiltInGroups { get; set; }

        [CheckboxParameter(ConfigParameterNames.IncludeAppGroups)]
        public bool IncludeAppGroups { get; set; }

        [DividerParameter]
        [Required]
        [DefaultValue("Deactivate")]
        [DropdownParameter(ConfigParameterNames.UserDeprovisioningAction, false, new string[] { "Delete", "Deactivate" })]
        public string UserDeprovisioningAction { get; set; }

        [CheckboxParameter(ConfigParameterNames.ActivateNewUsers)]
        public bool ActivateNewUsers { get; set; }

        [CheckboxParameter(ConfigParameterNames.SendActivationEmailToNewUsers)]
        public bool SendActivationEmailToNewUsers { get; set; }
    }
}