using System;
using System.Security.Cryptography;
using System.Text;

namespace VizzyGPT.Runtime.Security
{
    public static class DpapiSecretProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VizzyGPT/0.1");

        public static string Protect(string secret)
        {
            if (secret == null)
            {
                throw new ArgumentNullException(nameof(secret));
            }

            var protectedBytes = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string Unprotect(string protectedSecret)
        {
            if (protectedSecret == null)
            {
                throw new ArgumentNullException(nameof(protectedSecret));
            }

            var secretBytes = ProtectedData.Unprotect(
                Convert.FromBase64String(protectedSecret),
                Entropy,
                DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(secretBytes);
        }
    }
}
