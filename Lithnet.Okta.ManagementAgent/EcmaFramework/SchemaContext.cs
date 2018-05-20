using System.Collections.Concurrent;
using System.Threading;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class SchemaContext
    {
        public object ConnectionContext { get; internal set; }

        public MAConfigParameters ConfigParameters { get; internal set; }
    }
}
