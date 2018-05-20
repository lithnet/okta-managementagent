using System;
using System.Collections.ObjectModel;
using System.Security;
using Microsoft.MetadirectoryServices;
using NLog;

namespace Lithnet.Okta.ManagementAgent
{
    public class Ecma2Password :
        IMAExtensible2Password
    {
        private static readonly Logger logger = LogManager.GetCurrentClassLogger();

        private PasswordContext passwordContext;

        public void OpenPasswordConnection(KeyedCollection<string, ConfigParameter> configParameters, Partition partition)
        {
            MAConfigParameters maConfigParameters = new MAConfigParameters(configParameters);
            Logging.SetupLogger(maConfigParameters);
            this.passwordContext = new PasswordContext()
            {
                ConnectionContext = CSEntryPassword.GetConnectionContext(maConfigParameters),
                ConfigParameters = maConfigParameters
            };
        }

        public void ClosePasswordConnection()
        {
        }

        public ConnectionSecurityLevel GetConnectionSecurityLevel()
        {
            return ConnectionSecurityLevel.Secure;
        }

        public void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options)
        {
            try
            {
                logger.Trace($"Setting password for: {csentry.DN}");
                CSEntryPassword.SetPassword(csentry, newPassword, options, this.passwordContext);
                logger.Info($"Successfully set password for: {csentry.DN}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error setting password for {csentry.DN}");
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }

        public void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            try
            {
                logger.Info($"Changing password for: {csentry.DN}");
                CSEntryPassword.ChangePassword(csentry, oldPassword, newPassword, this.passwordContext);
                logger.Info($"Successfully changed password for: {csentry.DN}");
            }
            catch (Exception ex)
            {
                logger.Error($"Error changing password for {csentry.DN}");
                logger.Error(ex.UnwrapIfSingleAggregateException());
                throw;
            }
        }
    }
}