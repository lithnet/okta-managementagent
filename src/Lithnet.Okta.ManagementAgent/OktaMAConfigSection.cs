﻿using System.Configuration;

namespace Lithnet.Okta.ManagementAgent
{
    internal class OktaMAConfigSection : ConfigurationSection
    {
        private const string SectionName = "lithnet-okta-ma";
        private const string PropHttpDebugEnabled = "http-debug-enabled";
        private const string PropProxyUrl = "proxy-url";
        private const string PropUserListPageSize = "user-list-page-size";
        private const string PropGroupListPageSize = "group-list-page-size";
        private const string PropConnectionLimit = "connection-limit";

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

        [ConfigurationProperty(OktaMAConfigSection.PropConnectionLimit, IsRequired = false, DefaultValue = 1000)]
        public int ConnectionLimit => (int)this[OktaMAConfigSection.PropConnectionLimit];

        [ConfigurationProperty(OktaMAConfigSection.PropUserListPageSize, IsRequired = false, DefaultValue = 200)]
        public int UserListPageSize => (int)this[OktaMAConfigSection.PropUserListPageSize];

        [ConfigurationProperty(OktaMAConfigSection.PropGroupListPageSize, IsRequired = false, DefaultValue = -1)]
        public int GroupListPageSize => (int)this[OktaMAConfigSection.PropGroupListPageSize];
    }
}