using System.Collections.Concurrent;
using System.Threading;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    internal interface IExportConnectionContext
    {
        MAConfigParameters configParameters { get; }

        CancellationToken cancellationToken { get; }
    }
}
