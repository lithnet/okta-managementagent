using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class CSEntryPassword
    {
        public static void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options, IConnectionContext connectionContext)
        {
            User u = new User
            {
                Credentials = new UserCredentials()
                {
                    Password = new PasswordCredential()
                    {
                        Value = newPassword.ConvertToUnsecureString()
                    }
                }
            };

            ((OktaConnectionContext)connectionContext).Client.Users.UpdateUserAsync(u, csentry.DN.ToString()).Wait();
        }

        public static void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword, IConnectionContext connectionContext)
        {
            ((OktaConnectionContext)connectionContext).Client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                new ChangePasswordOptions()
                {
                    NewPassword = newPassword.ConvertToUnsecureString(),
                    CurrentPassword = oldPassword.ConvertToUnsecureString()
                }).Wait();
        }

        public static IConnectionContext GetConnectionContext(MAConfigParameters configParameters)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
