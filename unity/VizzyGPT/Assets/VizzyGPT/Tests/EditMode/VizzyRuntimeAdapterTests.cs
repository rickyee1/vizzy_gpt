using System.IO;
using System.Collections.Generic;
using System.Linq;
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
        public void Complete_editor_contract_uses_the_stock_program_loader_when_available()
        {
            var host = new GameObject("Reloading Vizzy Editor");
            try
            {
                var editor = host.AddComponent<ReloadingVizzyEditor>();
                editor.Initialize(File.ReadAllText(FixturePath));
                const string replacementXml =
                    "<Program name='New Program'><Variables /><Instructions>" +
                    "<Event event='FlightStart' id='0' style='flight-start' />" +
                    "<SetInput input='throttle' id='1' style='set-input'><Constant number='0.5' /></SetInput>" +
                    "</Instructions><Expressions /></Program>";

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    replacementXml,
                    out var error);

                Assert.That(applied, Is.True, error);
                Assert.That(editor.LoadCount, Is.EqualTo(1));
                Assert.That(editor.RefreshCount, Is.Zero);
                var serialized = new ProgramSerializer().SerializeFlightProgram(editor.FlightProgram);
                Assert.That(
                    serialized.Descendants("SetInput").Single().Attribute("input")?.Value,
                    Is.EqualTo("throttle"));
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Stock_program_loader_does_not_require_a_writable_flight_program_member()
        {
            var host = new GameObject("Read Only Reloading Vizzy Editor");
            try
            {
                var editor = host.AddComponent<ReadOnlyReloadingVizzyEditor>();
                editor.Initialize(File.ReadAllText(FixturePath));

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    ReplacementXml,
                    out var error);

                Assert.That(applied, Is.True, error);
                Assert.That(editor.LoadCount, Is.EqualTo(1));
                AssertThrottleReplacement(editor.FlightProgram);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Stock_program_loader_does_not_require_a_separate_refresh_method()
        {
            var host = new GameObject("Loader Only Vizzy Editor");
            try
            {
                var editor = host.AddComponent<LoaderOnlyVizzyEditor>();
                editor.Initialize(File.ReadAllText(FixturePath));

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    ReplacementXml,
                    out var error);

                Assert.That(applied, Is.True, error);
                Assert.That(editor.LoadCount, Is.EqualTo(1));
                AssertThrottleReplacement(editor.FlightProgram);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Stock_program_loader_failure_restores_the_previous_program()
        {
            var host = new GameObject("Fail Once Vizzy Editor");
            try
            {
                var originalXml = File.ReadAllText(FixturePath);
                var editor = host.AddComponent<FailOnceVizzyEditor>();
                editor.Initialize(originalXml);

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    ReplacementXml,
                    out var error);

                Assert.That(applied, Is.False);
                Assert.That(error, Does.Contain("previous program was restored"));
                Assert.That(editor.LoadCount, Is.EqualTo(2));
                Assert.That(
                    XNode.DeepEquals(
                        XElement.Parse(originalXml),
                        new ProgramSerializer().SerializeFlightProgram(editor.FlightProgram)),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Stock_program_loader_reports_when_apply_and_rollback_both_fail()
        {
            var host = new GameObject("Always Failing Vizzy Editor");
            try
            {
                var editor = host.AddComponent<AlwaysFailingVizzyEditor>();
                editor.Initialize(File.ReadAllText(FixturePath));

                var applied = new VizzyRuntimeAdapter().TrySetEditorProgramXml(
                    ReplacementXml,
                    out var error);

                Assert.That(applied, Is.False);
                Assert.That(error, Does.Contain("rollback failed"));
                Assert.That(editor.LoadCount, Is.EqualTo(2));
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

        private const string ReplacementXml =
            "<Program name='New Program'><Variables /><Instructions>" +
            "<Event event='FlightStart' id='0' style='flight-start' />" +
            "<SetInput input='throttle' id='1' style='set-input'><Constant number='0.5' /></SetInput>" +
            "</Instructions><Expressions /></Program>";

        private static void AssertThrottleReplacement(FlightProgram program)
        {
            var serialized = new ProgramSerializer().SerializeFlightProgram(program);
            Assert.That(
                serialized.Descendants("SetInput").Single().Attribute("input")?.Value,
                Is.EqualTo("throttle"));
        }
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

    public sealed class ReloadingVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; private set; }

        public int LoadCount { get; private set; }

        public int RefreshCount { get; private set; }

        public void Initialize(string xml)
        {
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(XElement.Parse(xml));
        }

        public void LoadFlightProgram(XElement programXml)
        {
            LoadCount++;
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(programXml);
        }

        public void RefreshUI()
        {
            RefreshCount++;
        }
    }

    public sealed class ReadOnlyReloadingVizzyEditor : MonoBehaviour
    {
        private FlightProgram flightProgram;

        public FlightProgram FlightProgram => flightProgram;

        public int LoadCount { get; private set; }

        public void Initialize(string xml)
        {
            flightProgram = new ProgramSerializer().DeserializeFlightProgram(XElement.Parse(xml));
        }

        public void LoadFlightProgram(XElement programXml)
        {
            LoadCount++;
            flightProgram = new ProgramSerializer().DeserializeFlightProgram(programXml);
        }

        public void RefreshUI()
        {
        }
    }

    public sealed class LoaderOnlyVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; private set; }

        public int LoadCount { get; private set; }

        public void Initialize(string xml)
        {
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(XElement.Parse(xml));
        }

        public void LoadFlightProgram(XElement programXml)
        {
            LoadCount++;
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(programXml);
        }
    }

    public sealed class FailOnceVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; private set; }

        public int LoadCount { get; private set; }

        public void Initialize(string xml)
        {
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(XElement.Parse(xml));
        }

        public void LoadFlightProgram(XElement programXml)
        {
            LoadCount++;
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(programXml);
            if (LoadCount == 1)
            {
                throw new InvalidDataException("Simulated loader failure.");
            }
        }
    }

    public sealed class AlwaysFailingVizzyEditor : MonoBehaviour
    {
        public FlightProgram FlightProgram { get; private set; }

        public int LoadCount { get; private set; }

        public void Initialize(string xml)
        {
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(XElement.Parse(xml));
        }

        public void LoadFlightProgram(XElement programXml)
        {
            LoadCount++;
            FlightProgram = new ProgramSerializer().DeserializeFlightProgram(programXml);
            throw new InvalidDataException("Simulated loader failure.");
        }
    }
}
