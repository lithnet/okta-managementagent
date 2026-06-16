using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Lithnet.Ecma2Framework;
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
                        Value = ConvertToUnsecureString(newPassword)
                    }
                }
            };

            string dn = csentry.DN == null ? null : csentry.DN.ToString();
            await this.client.Users.UpdateUserAsync(u, dn, CancellationToken.None);
        }

        public async Task ChangePasswordAsync(CSEntry csentry, SecureString oldPassword, SecureString newPassword)
        {
            string dn = csentry.DN == null ? null : csentry.DN.ToString();
            await this.client.Users.ChangePasswordAsync(dn,
                new ChangePasswordOptions()
                {
                    NewPassword = ConvertToUnsecureString(newPassword),
                    CurrentPassword = ConvertToUnsecureString(oldPassword)
                });
        }

        private static string ConvertToUnsecureString(SecureString securePassword)
        {
            if (securePassword == null)
            {
                throw new ArgumentNullException(nameof(securePassword));
            }

            IntPtr unmanagedString = IntPtr.Zero;

            try
            {
                unmanagedString = Marshal.SecureStringToGlobalAllocUnicode(securePassword);
                return Marshal.PtrToStringUni(unmanagedString);
            }
            finally
            {
                Marshal.ZeroFreeGlobalAllocUnicode(unmanagedString);
            }
        }
    }
}
