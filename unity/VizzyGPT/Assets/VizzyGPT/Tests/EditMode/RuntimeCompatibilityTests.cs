#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class RuntimeCompatibilityTests
    {
        [Test]
        public void Compatible_contract_keeps_modify_available()
        {
            using var workflow = CreateWorkflow(RuntimeCompatibilityResult.Compatible(
                "Assets.Scripts.Vizzy.UI.VizzyUIScript.FlightProgram"));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);

            Assert.That(workflow.Mode, Is.EqualTo(VizzyGptPanelMode.Modify));
            Assert.That(workflow.CurrentRenderState.CanModify, Is.True);
        }

        [TestCase(
            RuntimeCompatibilityKind.EditorUnavailable,
            "Vizzy editor contract unavailable: expected one public FlightProgram member with a refresh method.")]
        [TestCase(
            RuntimeCompatibilityKind.AmbiguousContract,
            "Vizzy editor contract ambiguous: First.Vizzy.FlightProgram, Second.Vizzy.FlightProgram.")]
        public void Incompatible_contract_keeps_ask_and_hides_modify_preview(
            RuntimeCompatibilityKind kind,
            string expectedDiagnostic)
        {
            var compatibility = kind == RuntimeCompatibilityKind.EditorUnavailable
                ? RuntimeCompatibilityResult.EditorUnavailable()
                : RuntimeCompatibilityResult.AmbiguousContract(
                    "Second.Vizzy.FlightProgram",
                    "First.Vizzy.FlightProgram");
            using var workflow = CreateWorkflow(compatibility);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);

            Assert.That(workflow.Mode, Is.EqualTo(VizzyGptPanelMode.Ask));
            Assert.That(workflow.CurrentRenderState.CanSend, Is.True);
            Assert.That(workflow.CurrentRenderState.CanModify, Is.False);
            Assert.That(workflow.CurrentRenderState.CanPreview, Is.False);
            Assert.That(workflow.StatusText, Is.EqualTo(expectedDiagnostic));
            Assert.That(workflow.StatusText, Does.Not.Contain("Exception"));
            Assert.That(workflow.StatusText, Does.Not.Contain("Bearer"));
        }

        private static VizzyGptPanelWorkflow CreateWorkflow(RuntimeCompatibilityResult compatibility)
        {
            return new VizzyGptPanelWorkflow(
                new ReadOnlyAdapter(),
                (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                (prompt, context) => new AiRequest(
                    ApiMode.Responses,
                    prompt,
                    context,
                    "test",
                    new Uri("https://example.invalid"),
                    "secret",
                    TimeSpan.FromSeconds(10)),
                (_, __, ___, ____) => Task.CompletedTask,
                () => VizzyNodeCatalog.FromToolboxXml("<Items />"),
                () => DateTime.UtcNow,
                _ => { },
                null,
                compatibility);
        }

        private sealed class ReadOnlyAdapter : IVizzyRuntimeAdapter
        {
            public bool IsEditorAvailable => false;
            public bool IsFlightAvailable => false;

            public bool TryGetEditorProgramXml(out string xml, out string error)
            {
                xml = string.Empty;
                error = "unavailable";
                return false;
            }

            public bool TrySetEditorProgramXml(string xml, out string error)
            {
                error = "unavailable";
                return false;
            }

            public bool TryGetFlightProgramXml(out string xml, out string error)
            {
                xml = string.Empty;
                error = "unavailable";
                return false;
            }

            public ValidationIssue? ValidateWithProgramSerializer(string xml)
            {
                return null;
            }
        }
    }
}
