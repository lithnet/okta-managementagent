using System;

namespace Lithnet.Okta.ManagementAgent
{
    internal class GroupWatermark
    {
        public DateTime? LastUpdated { get; set; }

        public DateTime? LastMembershipUpdated { get; set; }
    }
}
