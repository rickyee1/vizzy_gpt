using System.IO;
using NUnit.Framework;
using UnityEngine;
using VizzyGPT.Runtime.Storage;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class JunoDataPathsTests
    {
        [Test]
        public void Root_is_nested_under_the_current_Juno_persistent_directory()
        {
            var expected = Path.Combine(Application.persistentDataPath, "UserData", "VizzyGPT");

            Assert.That(JunoDataPaths.Root, Is.EqualTo(expected));
        }
    }
}
