using System.IO;
using System.Linq;
using System.Xml.Linq;
using ModApi.Craft.Program;
using NUnit.Framework;
using UnityEngine;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class ProgramSerializerContractTests
    {
        [Test]
        public void Minimal_program_round_trips_through_current_mod_api()
        {
            var path = Path.Combine(Application.dataPath, "VizzyGPT", "Tests", "Fixtures", "minimal.xml");
            var xml = File.ReadAllText(path);
            var serializer = new ProgramSerializer();

            var program = serializer.DeserializeFlightProgram(XElement.Parse(xml));
            var serialized = serializer.SerializeFlightProgram(program);

            Assert.That(serialized.Name.LocalName, Is.EqualTo("Program"));
            Assert.That(
                serialized.Descendants("Event").Single().Attribute("id")?.Value,
                Is.EqualTo("0"));
        }
    }
}
