using System.Collections.Concurrent;
using System.Threading;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    internal interface IImportConnectionContext
    {
        bool InDelta { get; }

        MAConfigParameters configParameters { get; }

        WatermarkKeyedCollection importState { get; }
        
        Schema importTypes { get; }

        CancellationToken cancellationToken { get; }
        
        BlockingCollection<CSEntryChange> importItems { get; }

        object CustomContext { get; }
    }
}
