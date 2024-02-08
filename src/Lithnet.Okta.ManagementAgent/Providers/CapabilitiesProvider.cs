using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    internal class CapabilitiesProvider : ICapabilitiesProvider
    {
        public Task<MACapabilities> GetCapabilitiesAsync(IConfigParameters configParameters)
        {
            return Task.FromResult(new MACapabilities
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
            });
        }
    }
}
