using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
using Lithnet.MetadirectoryServices;
using Microsoft.MetadirectoryServices;
using Okta.Sdk;

namespace Lithnet.Okta.ManagementAgent
{
    internal class UserPasswordProvider : IObjectPasswordProvider
    {
        private readonly IOktaClient client;
        private PasswordContext context;

        public UserPasswordProvider(OktaClientProvider clientProvider)
        {
            this.client = clientProvider.GetClient();
        }

        public Task InitializeAsync(PasswordContext context)
        {
            this.context = context;
            return Task.CompletedTask;
        }

        public Task<bool> CanPerformPasswordOperationAsync(CSEntry csentry)
        {
            return Task.FromResult(csentry.ObjectType == "user");
        }

        public async Task SetPasswordAsync(CSEntry csentry, SecureString newPassword, PasswordOptions options)
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

            await this.client.Users.UpdateUserAsync(u, csentry.DN.ToString(), CancellationToken.None);
        }

        public async Task ChangePasswordAsync(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            await this.client.Users.ChangePasswordAsync(csentry.DN.ToString(),
                new ChangePasswordOptions()
                {
                    NewPassword = newPassword.ConvertToUnsecureString(),
                    CurrentPassword = oldPassword.ConvertToUnsecureString()
                });
        }
    }
}