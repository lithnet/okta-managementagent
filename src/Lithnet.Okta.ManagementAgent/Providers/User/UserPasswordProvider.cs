using System.Security;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    public class UserPasswordProvider : IObjectPasswordProvider
    {
        private IPasswordContext context;
        private IOktaClient client;

        public void Initialize(IPasswordContext context)
        {
            this.context = context;
            this.client = ((OktaConnectionContext)this.context.ConnectionContext).Client;
        }

        public bool CanPerformPasswordOperation(CSEntry csentry)
        {
            return csentry.ObjectType == "user";
        }

        public void SetPassword(CSEntry csentry, SecureString newPassword, PasswordOptions options)
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

            AsyncHelper.RunSync(this.client.Users.UpdateUserAsync(u, csentry.DN.ToString()));
        }

        public void ChangePassword(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            AsyncHelper.RunSync(this.client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                new ChangePasswordOptions()
                {
                    NewPassword = newPassword.ConvertToUnsecureString(),
                    CurrentPassword = oldPassword.ConvertToUnsecureString()
                }));
        }
    }
}