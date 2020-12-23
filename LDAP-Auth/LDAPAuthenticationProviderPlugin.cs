using System;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using Jellyfin.Data.Enums;
using MediaBrowser.Common;
using MediaBrowser.Controller.Authentication;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;
using Novell.Directory.Ldap;

namespace Jellyfin.Plugin.LDAP_Auth
{
    /// <summary>
    /// Ldap Authentication Provider Plugin.
    /// </summary>
    public class LdapAuthenticationProviderPlugin : IAuthenticationProvider
    {
        private readonly ILogger<LdapAuthenticationProviderPlugin> _logger;
        private readonly IApplicationHost _applicationHost;

        /// <summary>
        /// Initializes a new instance of the <see cref="LdapAuthenticationProviderPlugin"/> class.
        /// </summary>
        /// <param name="applicationHost">Instance of the <see cref="IApplicationHost"/> interface.</param>
        /// <param name="logger">Instance of the <see cref="ILogger{LdapAuthenticationProviderPlugin}"/> interface.</param>
        public LdapAuthenticationProviderPlugin(IApplicationHost applicationHost, ILogger<LdapAuthenticationProviderPlugin> logger)
        {
            _logger = logger;
            _applicationHost = applicationHost;
        }

        private string[] LdapUsernameAttributes => LdapPlugin.Instance.Configuration.LdapSearchAttributes.Replace(" ", string.Empty, StringComparison.Ordinal).Split(',');

        private string UsernameAttr => LdapPlugin.Instance.Configuration.LdapUsernameAttribute;

        private string SearchFilter => LdapPlugin.Instance.Configuration.LdapSearchFilter;

        private string AdminFilter => LdapPlugin.Instance.Configuration.LdapAdminFilter;

        /// <summary>
        /// Gets plugin name.
        /// </summary>
        public string Name => "LDAP-Authentication";

        /// <summary>
        /// Gets a value indicating whether gets plugin enabled.
        /// </summary>
        public bool IsEnabled => true;

        /// <summary>
        /// Authenticate user against the ldap server.
        /// </summary>
        /// <param name="username">Username to authenticate.</param>
        /// <param name="password">Password to authenticate.</param>
        /// <returns>A <see cref="ProviderAuthenticationResult"/> with the authentication result.</returns>
        /// <exception cref="AuthenticationException">Exception when failing to authenticate.</exception>
        public async Task<ProviderAuthenticationResult> Authenticate(string username, string password)
        {
            var userManager = _applicationHost.Resolve<IUserManager>();
            User user = null;
            var ldapUser = LocateLdapUser(username);

            var ldapUsername = GetAttribute(ldapUser, UsernameAttr)?.StringValue;
            _logger.LogDebug("Setting username: {1}", ldapUsername);

            try
            {
                user = userManager.GetUserByName(ldapUsername);
            }
            catch (Exception e)
            {
                _logger.LogWarning("User Manager could not find a user for LDAP User, this may not be fatal", e);
            }

            using var ldapClient = new LdapConnection { SecureSocketLayer = LdapPlugin.Instance.Configuration.UseSsl };
            _logger.LogDebug("Trying bind as user {1}", ldapUser.Dn);
            try
            {
                if (LdapPlugin.Instance.Configuration.SkipSslVerify)
                {
                    ldapClient.UserDefinedServerCertValidationDelegate += LdapClient_UserDefinedServerCertValidationDelegate;
                }

                ldapClient.Connect(LdapPlugin.Instance.Configuration.LdapServer, LdapPlugin.Instance.Configuration.LdapPort);
                if (LdapPlugin.Instance.Configuration.UseStartTls)
                {
                    ldapClient.StartTls();
                }

                ldapClient.Bind(ldapUser.Dn, password);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to Connect or Bind to server as user {1}", ldapUser.Dn);
                throw new AuthenticationException("Error completing LDAP login. Invalid username or password.", e);
            }
            finally
            {
                ldapClient.UserDefinedServerCertValidationDelegate -= LdapClient_UserDefinedServerCertValidationDelegate;
            }

            if (ldapClient.Bound)
            {
                if (user == null)
                {
                    // Determine if the user should be an administrator
                    var ldapIsAdmin = false;

                    // Search the current user DN with the adminFilter
                    var ldapUsers = ldapClient.Search(
                        ldapUser.Dn,
                        0,
                        AdminFilter,
                        LdapUsernameAttributes,
                        false);

                    // If we got non-zero, then the filter matched and the user is an admin
                    if (ldapUsers.Count != 0)
                    {
                        ldapIsAdmin = true;
                    }

                    _logger.LogDebug("Creating new user {1} - is admin? {2}", ldapUsername, ldapIsAdmin);
                    if (LdapPlugin.Instance.Configuration.CreateUsersFromLdap)
                    {
                        user = await userManager.CreateUserAsync(ldapUsername).ConfigureAwait(false);
                        user.AuthenticationProviderId = GetType().FullName;
                        user.SetPermission(PermissionKind.IsAdministrator, ldapIsAdmin);
                        await userManager.UpdateUserAsync(user).ConfigureAwait(false);
                    }
                    else
                    {
                        _logger.LogError($"User not configured for LDAP Uid: {ldapUsername}");
                        throw new AuthenticationException(
                            $"Automatic User Creation is disabled and there is no Jellyfin user for authorized Uid: {ldapUsername}");
                    }
                }

                return new ProviderAuthenticationResult { Username = ldapUsername };
            }

            _logger.LogError("Error logging in, invalid LDAP username or password");
            throw new AuthenticationException("Error completing LDAP login. Invalid username or password.");
        }

        /// <inheritdoc />
        public bool HasPassword(User user)
        {
            return true;
        }

        /// <inheritdoc />
        public Task ChangePassword(User user, string newPassword)
        {
            throw new NotImplementedException();
        }

        private static bool LdapClient_UserDefinedServerCertValidationDelegate(
            object sender,
            System.Security.Cryptography.X509Certificates.X509Certificate certificate,
            System.Security.Cryptography.X509Certificates.X509Chain chain,
            System.Net.Security.SslPolicyErrors sslPolicyErrors)
            => true;

        private LdapEntry LocateLdapUser(string username)
        {
            var foundUser = false;
            LdapEntry ldapUser = null;
            using var ldapClient = new LdapConnection { SecureSocketLayer = LdapPlugin.Instance.Configuration.UseSsl };
            try
            {
                if (LdapPlugin.Instance.Configuration.SkipSslVerify)
                {
                    ldapClient.UserDefinedServerCertValidationDelegate +=
                        LdapClient_UserDefinedServerCertValidationDelegate;
                }

                ldapClient.Connect(LdapPlugin.Instance.Configuration.LdapServer, LdapPlugin.Instance.Configuration.LdapPort);
                if (LdapPlugin.Instance.Configuration.UseStartTls)
                {
                    ldapClient.StartTls();
                }

                ldapClient.Bind(LdapPlugin.Instance.Configuration.LdapBindUser, LdapPlugin.Instance.Configuration.LdapBindPassword);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to Connect or Bind to server");
                throw new AuthenticationException("Failed to Connect or Bind to server");
            }
            finally
            {
                ldapClient.UserDefinedServerCertValidationDelegate -= LdapClient_UserDefinedServerCertValidationDelegate;
            }

            if (!ldapClient.Connected)
            {
                return null;
            }

            var ldapUsers =
                ldapClient.Search(LdapPlugin.Instance.Configuration.LdapBaseDn, 2, SearchFilter, LdapUsernameAttributes, false);
            if (ldapUsers == null)
            {
                _logger.LogWarning("No LDAP users found from query");
                throw new AuthenticationException("No users found in LDAP Query");
            }

            _logger.LogDebug("Search: {1} {2} @ {3}", LdapPlugin.Instance.Configuration.LdapBaseDn, SearchFilter, LdapPlugin.Instance.Configuration.LdapServer);

            while (ldapUsers.HasMore() && foundUser == false)
            {
                var currentUser = ldapUsers.Next();
                foreach (var attr in LdapUsernameAttributes)
                {
                    var toCheck = GetAttribute(currentUser, attr);
                    if (toCheck?.StringValueArray != null)
                    {
                        foreach (var name in toCheck.StringValueArray)
                        {
                            if (username == name)
                            {
                                ldapUser = currentUser;
                                foundUser = true;
                            }
                        }
                    }
                }
            }

            if (foundUser == false)
            {
                _logger.LogError("Found no users matching {1} in LDAP search.", username);
                throw new AuthenticationException("Found no LDAP users matching provided username.");
            }

            return ldapUser;
        }

        private LdapAttribute GetAttribute(LdapEntry userEntry, string attr)
        {
            try
            {
                return userEntry.GetAttribute(attr);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error getting LDAP attribute");
                return null;
            }
        }
    }
}
