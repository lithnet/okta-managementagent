using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.MetadirectoryServices;
using NLog;

namespace Lithnet.Okta.ManagementAgent
{
    public class Ecma2 :
        IMAExtensible2GetSchema,
        IMAExtensible2GetCapabilitiesEx,
        IMAExtensible2GetParameters
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();
     
        public Schema GetSchema(KeyedCollection<string, ConfigParameter> configParameters)
        {
            try
            {
                MAConfigParameters maconfigParameters = new MAConfigParameters(configParameters);
                Logging.SetupLogger(maconfigParameters);

                SchemaContext context = new SchemaContext()
                {
                    ConfigParameters = maconfigParameters,
                    ConnectionContext = MASchema.GetConnectionContext(maconfigParameters)
                };

                return MASchema.GetMmsSchema(context);
            }
            catch (Exception ex)
            {
                logger.Error(ex.UnwrapIfSingleAggregateException(), "Could not retrieve schema");
                throw;
            }
        }

        public IList<ConfigParameterDefinition> GetConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return MAConfigParameters.GetConfigParameters(configParameters, page);
        }

        public ParameterValidationResult ValidateConfigParameters(KeyedCollection<string, ConfigParameter> configParameters, ConfigParameterPage page)
        {
            return MAConfigParameters.ValidateConfigParameters(configParameters, page);
        }

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