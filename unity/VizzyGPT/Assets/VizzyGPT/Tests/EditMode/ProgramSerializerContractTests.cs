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

        [Test]
        public void Current_mod_api_accepts_a_program_without_a_top_level_instruction_stack()
        {
            AssertProgramRoundTrips(
                "<Program><Variables /><Expressions /></Program>",
                expectedInstructionStackCount: 0);
        }

        [Test]
        public void Current_mod_api_preserves_multiple_top_level_instruction_stacks()
        {
            AssertProgramRoundTrips(
                "<Program><Variables /><Instructions><Event event='FlightStart' id='0' style='flight-start' /></Instructions>" +
                "<Instructions><Event event='FlightStart' id='1' style='flight-start' /></Instructions><Expressions /></Program>",
                expectedInstructionStackCount: 2);
        }

        private static void AssertProgramRoundTrips(string xml, int expectedInstructionStackCount)
        {
            var serializer = new ProgramSerializer();
            var program = serializer.DeserializeFlightProgram(XElement.Parse(xml));
            var serialized = serializer.SerializeFlightProgram(program);

            Assert.That(serialized.Elements("Instructions").Count(), Is.EqualTo(expectedInstructionStackCount));
        }
    }
}
