using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class ImportContext
    {
        public bool InDelta { get; internal set; }

        public MAConfigParameters ConfigParameters { get; internal set; }

        public WatermarkKeyedCollection IncomingWatermark { get; internal set; }

        public WatermarkKeyedCollection OutgoingWatermark { get; internal set; }
        
        public Schema Types { get; internal set; }

        public CancellationTokenSource CancellationTokenSource { get; internal set; }
        
        public BlockingCollection<CSEntryChange> ImportItems { get; internal set; }

        public object ConnectionContext { get; internal set; }

        internal Stopwatch Timer { get; } = new Stopwatch();

        internal int ImportedItemCount;

        internal TimeSpan ProducerDuration { get; set; }
    }
}
