using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class CapabilitiesProvider : ICapabilitiesProvider
    {
        public MACapabilities GetCapabilitiesEx(KeyedCollection<string, ConfigParameter> configParameters)
        {
            return new MACapabilities
            {
                ConcurrentOperation = true,
                DeltaImport = true,
                DistinguishedNameStyle = MADistinguishedNameStyle.Generic,
                Normalizations = MANormalizations.None,
                IsDNAsAnchor = false,
                SupportHierarchy = false,
                SupportImport = true,
                SupportPartitions = false,
                SupportPassword = true,
                ExportType = MAExportType.MultivaluedReferenceAttributeUpdate,
                ObjectConfirmation = MAObjectConfirmation.Normal,
                ObjectRename = false,
                SupportExport = true
            };
        }
    }
}
