using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Security;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public static class ConfigParameterNames
    {
        internal static readonly string LogFileName = "Log file";

        internal static readonly string ApiKey = "API key";

        internal static readonly string TenantUrl = "Tenant URL";

        internal static readonly string IncludeBuiltInGroups = "Include built-in groups";

        internal static readonly string IncludeAppGroups = "Include app groups";

        internal static readonly string UserDeprovisioningAction = "User deprovisioning action";

        internal static readonly string ActivateNewUsers = "Activate new users";
    }
}