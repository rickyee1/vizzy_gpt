using System.IO;
using System.Collections.Generic;
using System.Xml.Linq;
using ModApi.Craft.Program;
using NUnit.Framework;
using UnityEngine;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyRuntimeAdapterTests
    {
        [Test]
        public void Serializer_validation_returns_an_error_for_malformed_xml()
        {
            var adapter = new VizzyRuntimeAdapter();

            var issue = adapter.ValidateWithProgramSerializer("<Program>");

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Code, Is.EqualTo("RuntimeSerializer"));
        }

        [Test]
        public void Editor_with_flight_program_but_no_refresh_contract_is_incompatible()
        {
            var host = new GameObject("Incomplete Vizzy Editor");
            try
            {
                host.AddComponent<IncompleteVizzyEditor>();

                Assert.That(new VizzyRuntimeAdapter().IsEditorAvailable, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Complete_editor_contract_invokes_the_probed_refresh_method()
        {
            var host = new GameObject("Complete Vizzy Editor");
            try
            {
                var editor = host.AddComponent<CompleteVizzyEditor>();
                editor.FlightProgram = new ProgramSerializer().DeserializeFlightProgram(
                    XElement.Parse(File.ReadAllText(FixturePath)));

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    File.ReadAllText(FixturePath),
                    out var error);

                Assert.That(applied, Is.True, error);
                Assert.That(editor.RefreshCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Active_flight_craft_resolves_its_flight_program_script()
        {
            var inactive = new ProgramSerializer().DeserializeFlightProgram(
                XElement.Parse(File.ReadAllText(FixturePath)));
            var expected = new ProgramSerializer().DeserializeFlightProgram(
                XElement.Parse(File.ReadAllText(FixturePath)));
            var inactivePart = new object();
            var activePart = new object();
            var craft = new FlightCraftContract(
                new[]
                {
                    new FlightProgramContract(inactive, inactivePart),
                    new FlightProgramContract(expected, activePart)
                },
                activePart);

            var found = RuntimeContractProbe.TryFindFlightProgramOnCraft(craft, out var actual);

            Assert.That(found, Is.True);
            Assert.That(actual, Is.SameAs(expected));
        }

        private static string FixturePath => Path.Combine(
            Application.dataPath,
            "VizzyGPT",
            "Tests",
            "Fixtures",
            "minimal.xml");
    }

    public sealed class FlightCraftContract
    {
        public FlightCraftContract(IReadOnlyList<FlightProgramContract> scripts, object activePart)
        {
            FlightProgramScripts = scripts;
            ActiveCommandPod = new CommandPodContract(activePart);
        }

        public IReadOnlyList<FlightProgramContract> FlightProgramScripts { get; }

        public CommandPodContract ActiveCommandPod { get; }
    }

    public sealed class FlightProgramContract
    {
        public FlightProgramContract(FlightProgram flightProgram, object part)
        {
            FlightProgram = flightProgram;
            PartScript = new PartScriptContract(part);
        }

        public FlightProgram FlightProgram { get; }

        public PartScriptContract PartScript { get; }
    }

    public sealed class PartScriptContract
    {
        public PartScriptContract(object data)
        {
            Data = data;
        }

        public object Data { get; }
    }

    public sealed class CommandPodContract
    {
        public CommandPodContract(object part)
        {
            Part = part;
        }

        public object Part { get; }
    }

    public sealed class IncompleteVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; set; }
    }

    public sealed class CompleteVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; set; }

        public int RefreshCount { get; private set; }

        public void RefreshUI()
        {
            RefreshCount++;
        }
    }
}
