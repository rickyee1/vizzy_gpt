using NUnit.Framework;
using VizzyGPT.Core;

namespace VizzyGPT.Core.Tests
{
    public sealed class BuildInfoTests
    {
        [Test]
        public void Version_matches_first_release()
        {
            Assert.That(BuildInfo.Version, Is.EqualTo("0.1.0"));
        }
    }
}
