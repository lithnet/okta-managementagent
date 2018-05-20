using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;

namespace Lithnet.Okta.ManagementAgent
{
    public class ImportContext
    {
        public ImportContext()
        {
            this.OutgoingWatermark = new WatermarkKeyedCollection();
        }

        public bool InDelta => this.RunStep?.ImportType == OperationType.Delta;

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

        internal Task Producer { get; set; }

        public OpenImportConnectionRunStep RunStep { get; internal set; }
    }
}
