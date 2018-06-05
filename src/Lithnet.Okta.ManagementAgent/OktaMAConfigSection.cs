using System.Configuration;

namespace Lithnet.Okta.ManagementAgent
{
    internal class OktaMAConfigSection : ConfigurationSection
    {
        private const string SectionName = "lithnet-okta-ma";
        private const string PropHttpDebugEnabled = "http-debug-enabled";
        private const string PropProxyUrl = "proxy-url";
        private const string PropExportThreads = "export-threads";
        private const string PropConnectionLimit = "connection-limit";
        private const string PropHttpClientTimeout = "http-client-timeout";

        internal static OktaMAConfigSection GetConfiguration()
        {
            OktaMAConfigSection section = (OktaMAConfigSection)ConfigurationManager.GetSection(SectionName);

            if (section == null)
            {
                section = new OktaMAConfigSection();
            }

            return section;
        }

        internal static OktaMAConfigSection Configuration { get; private set; }

        static OktaMAConfigSection()
        {
            OktaMAConfigSection.Configuration = OktaMAConfigSection.GetConfiguration();
        }

        [ConfigurationProperty(OktaMAConfigSection.PropHttpDebugEnabled, IsRequired = false, DefaultValue = false)]
        public bool HttpDebugEnabled => (bool)this[OktaMAConfigSection.PropHttpDebugEnabled];

        [ConfigurationProperty(OktaMAConfigSection.PropProxyUrl, IsRequired = false, DefaultValue = null)]
        public string ProxyUrl => (string)this[OktaMAConfigSection.PropProxyUrl];

        [ConfigurationProperty(OktaMAConfigSection.PropExportThreads, IsRequired = false, DefaultValue = 30)]
        public int ExportThreads => (int)this[OktaMAConfigSection.PropExportThreads];
        
        [ConfigurationProperty(OktaMAConfigSection.PropConnectionLimit, IsRequired = false, DefaultValue = 1000)]
        public int ConnectionLimit => (int)this[OktaMAConfigSection.PropConnectionLimit];

        [ConfigurationProperty(OktaMAConfigSection.PropHttpClientTimeout, IsRequired = false, DefaultValue = 600)]
        public int HttpClientTimeout => (int)this[OktaMAConfigSection.PropHttpClientTimeout];
    }
}