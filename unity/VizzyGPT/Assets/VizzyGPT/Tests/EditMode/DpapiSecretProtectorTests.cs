using NUnit.Framework;
using VizzyGPT.Runtime.Security;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class DpapiSecretProtectorTests
    {
        [Test]
        public void Protect_and_unprotect_round_trip_a_user_secret()
        {
            const string secret = "sk-vizzygpt-test-secret";

            var protectedSecret = DpapiSecretProtector.Protect(secret);

            Assert.That(protectedSecret, Is.Not.EqualTo(secret));
            Assert.That(DpapiSecretProtector.Unprotect(protectedSecret), Is.EqualTo(secret));
        }
    }
}
