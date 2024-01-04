using System.Collections.ObjectModel;
using Lithnet.Ecma2Framework;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class OktaConnectionContextProvider : IConnectionContextProvider
    {
        IConnectionContext IConnectionContextProvider.GetConnectionContext(KeyedCollection<string, ConfigParameter> configParameters, ConnectionContextOperationType contextOperationType)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
