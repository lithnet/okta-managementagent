using System.Security;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public class UserPasswordProvider : IObjectPasswordProvider
    {
        public bool CanPerformPasswordOperation(CSEntry csentry)
        {
            return csentry.ObjectType == "user";
        }

        public void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options, PasswordContext context)
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

            ((OktaConnectionContext) context.ConnectionContext).Client.Users.UpdateUserAsync(u, csentry.DN.ToString()).Wait();
        }

        public void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword, PasswordContext context)
        {
            ((OktaConnectionContext) context.ConnectionContext).Client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                new ChangePasswordOptions()
                {
                    NewPassword = newPassword.ConvertToUnsecureString(),
                    CurrentPassword = oldPassword.ConvertToUnsecureString()
                }).Wait();
        }
    }
}