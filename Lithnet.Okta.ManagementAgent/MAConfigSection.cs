using System.Configuration;

namespace Lithnet.Okta.ManagementAgent
{
    internal class MAConfigSection : ConfigurationSection
    {
        private const string SectionName = "lithnet-okta-ma";
        private const string PropHttpDebugEnabled = "http-debug-enabled";
        private const string PropProxyUrl = "proxy-url";
        private const string PropExportThreads = "export-threads";

        internal static MAConfigSection GetConfiguration()
        {
            MAConfigSection section = (MAConfigSection)ConfigurationManager.GetSection(SectionName);

            if (section == null)
            {
                section = new MAConfigSection();
            }

            return section;
        }

        internal static MAConfigSection Configuration { get; private set; }

        static MAConfigSection()
        {
            MAConfigSection.Configuration = MAConfigSection.GetConfiguration();
        }

        [ConfigurationProperty(MAConfigSection.PropHttpDebugEnabled, IsRequired = false, DefaultValue = false)]
        public bool HttpDebugEnabled => (bool)this[MAConfigSection.PropHttpDebugEnabled];

        [ConfigurationProperty(MAConfigSection.PropProxyUrl, IsRequired = false, DefaultValue = null)]
        public string ProxyUrl => (string)this[MAConfigSection.PropProxyUrl];

        [ConfigurationProperty(MAConfigSection.PropExportThreads, IsRequired = false, DefaultValue = 30)]
        public int ExportThreads => (int)this[MAConfigSection.PropExportThreads];
    }
}