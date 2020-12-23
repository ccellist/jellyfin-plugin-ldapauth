using System;
using System.Collections.Generic;
using Jellyfin.Plugin.LDAP_Auth.Config;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.LDAP_Auth
{
    /// <summary>
    /// Ldap Plugin.
    /// </summary>
    public class LdapPlugin : BasePlugin<PluginConfiguration>, IHasWebPages
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LdapPlugin"/> class.
        /// </summary>
        /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
        /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/>interface.</param>
        public LdapPlugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
        {
            Instance = this;
        }

        /// <summary>
        /// Gets the plugin instance.
        /// </summary>
        public static LdapPlugin Instance { get; private set; }

        /// <inheritdoc />
        public override string Name => "LDAP-Auth";

        /// <inheritdoc />
        public override Guid Id => Guid.Parse("958aad66-3784-4d2a-b89a-a7b6fab6e25c");

        /// <inheritdoc />
        public IEnumerable<PluginPageInfo> GetPages()
        {
            return new[]
            {
                new PluginPageInfo
                {
                    Name = Name,
                    EmbeddedResourcePath = $"{GetType().Namespace}.Config.configPage.html"
                }
            };
        }
    }
}
