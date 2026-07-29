using System;
using System.Security.Cryptography;
using System.Text;

namespace VizzyGPT.Core.Programs
{
    public static class VizzyProgramHash
    {
        public static string Compute(VizzyProgramDocument document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalXml.Write(document.Root)));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                result.Append(value.ToString("x2"));
            }

            return result.ToString();
        }
    }
}
