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
        public static void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options, PasswordContext context)
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

            ((OktaConnectionContext)context.ConnectionContext).Client.Users.UpdateUserAsync(u, csentry.DN.ToString()).Wait();
        }

        public static void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword, PasswordContext context)
        {
            ((OktaConnectionContext)context.ConnectionContext).Client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                new ChangePasswordOptions()
                {
                    NewPassword = newPassword.ConvertToUnsecureString(),
                    CurrentPassword = oldPassword.ConvertToUnsecureString()
                }).Wait();
        }

        public static object GetConnectionContext(MAConfigParameters configParameters)
        {
            return OktaConnectionContext.GetConnectionContext(configParameters);
        }
    }
}
