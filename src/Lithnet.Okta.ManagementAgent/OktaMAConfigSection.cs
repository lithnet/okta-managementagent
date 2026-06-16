namespace Lithnet.Okta.ManagementAgent
{
    /// <summary>
    /// Hidden, advanced override settings for the Okta MA. In the net48 in-process v2 these lived in the
    /// host process's app.config under the &lt;lithnet-okta-ma&gt; ConfigurationSection. On the v3 net8
    /// worker there is no app.config ConfigurationSection equivalent, so these overrides are read from the
    /// worker's appsettings.json under the "OktaMA" section and bound to this options class via
    /// <c>IOptions&lt;OktaMAConfigSection&gt;</c>. None of these are required inputs; each has a sensible
    /// default that matches the previous ConfigurationSection default, so an absent appsettings.json or an
    /// absent "OktaMA" section yields the same behaviour as an unconfigured v2 install.
    /// </summary>
    internal class OktaMAConfigSection
    {
        public const string SectionName = "OktaMA";

        public bool HttpDebugEnabled { get; set; } = false;

        public string ProxyUrl { get; set; } = null;

        public int ConnectionLimit { get; set; } = 1000;

        public int UserListPageSize { get; set; } = 200;

        public int GroupListPageSize { get; set; } = -1;
    }
}
