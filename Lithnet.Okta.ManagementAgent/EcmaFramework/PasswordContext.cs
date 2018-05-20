using System.Collections.Concurrent;
using System.Threading;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class PasswordContext
    {
        public object ConnectionContext { get; internal set; }

        public MAConfigParameters ConfigParameters { get; internal set; }
    }
}
