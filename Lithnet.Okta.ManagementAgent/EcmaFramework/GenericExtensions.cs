using System;
using System.Runtime.InteropServices;
using System.Security;

namespace Lithnet.Okta.ManagementAgent
{
    internal static class GenericExtensions
    {
        public static string ConvertToUnsecureString(this SecureString securePassword)
        {
            if (securePassword == null)
                throw new ArgumentNullException(nameof(securePassword));

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

        public static Exception UnwrapIfSingleAggregateException(this Exception ex)
        {
            if (ex is AggregateException aex)
            {
                if (aex.InnerExceptions.Count == 1)
                {
                    return aex.InnerException;
                }
            }

            return ex;
        }
    }
}