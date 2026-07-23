#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class PendingChangeFlowTests
    {
        private const string BaseXml =
            "<Program><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>";
        private static readonly DateTime Now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Flight_modify_saves_pending_without_mutating_the_running_program()
        {
            PendingChange? saved = null;
            AiRequest? observed = null;
            var adapter = new FakeAdapter(BaseXml, editorAvailable: false, flightAvailable: true);
            var environment = FlightEnvironment(
                (_, __) => Task.FromResult<PendingChange?>(null),
                (pending, _) => { saved = pending; return Task.CompletedTask; });
            using var workflow = CreateWorkflow(adapter, environment, (request, _) =>
            {
                observed = request;
                return Task.FromResult(ResponseFor(BaseXml));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("prepare a change").GetAwaiter().GetResult();

            Assert.That(observed!.Context, Does.Contain("FLIGHT CONTEXT"));
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.ProgramFingerprint, Is.EqualTo("craft-alpha"));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Flight_ask_includes_launch_program_and_bounded_flight_context()
        {
            AiRequest? observed = null;
            var environment = FlightEnvironment(
                (_, __) => Task.FromResult<PendingChange?>(null),
                (_, __) => Task.CompletedTask);
            using var workflow = CreateWorkflow(
                new FakeAdapter(BaseXml, false, true),
                environment,
                (request, _) =>
                {
                    observed = request;
                    return Task.FromResult(new AiResponse("Analysis.", null, false, Array.Empty<string>()));
                });

            workflow.OpenPanel();
            workflow.SendPromptAsync("Explain the flight behavior.").GetAwaiter().GetResult();

            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Context, Does.Contain("<Log id=\"1\" text=\"before\""));
            Assert.That(observed.Context, Does.Contain("FLIGHT CONTEXT"));
            Assert.That(observed.Context, Does.Contain("Ask mode does not permit program mutation."));
            Assert.That(workflow.CanApply, Is.False);
        }

        [Test]
        public void Existing_flight_pending_is_not_replaced_before_an_explicit_preview_apply()
        {
            var existing = CreatePending(BaseXml, AddVariablePatch(BaseXml), "craft-alpha");
            var saves = 0;
            var environment = FlightEnvironment(
                (_, __) => Task.FromResult<PendingChange?>(existing),
                (_, __) => { saves++; return Task.CompletedTask; });
            using var workflow = CreateWorkflow(
                new FakeAdapter(BaseXml, false, true),
                environment,
                (_, __) => Task.FromResult(ResponseFor(BaseXml)));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("replace pending").GetAwaiter().GetResult();

            Assert.That(workflow.StatusText, Does.Contain("replace"));
            Assert.That(saves, Is.EqualTo(0));
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(saves, Is.EqualTo(1));
        }

        [Test]
        public void Flight_modify_resolves_the_launch_snapshot_after_flight_initialization()
        {
            var snapshotReads = 0;
            AiRequest? observed = null;
            var environment = new VizzyGptWorkflowEnvironment(
                true,
                "craft-alpha",
                () =>
                {
                    snapshotReads++;
                    return BaseXml;
                },
                () => "FLIGHT CONTEXT\nTelemetry summaries:",
                (_, __) => Task.FromResult<PendingChange?>(null),
                (_, __) => Task.CompletedTask,
                (_, __) => Task.CompletedTask);
            using var workflow = CreateWorkflow(
                new FakeAdapter(BaseXml, false, true),
                environment,
                (request, _) =>
                {
                    observed = request;
                    return Task.FromResult(ResponseFor(BaseXml));
                });

            Assert.That(snapshotReads, Is.EqualTo(0));
            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("late flight initialization").GetAwaiter().GetResult();

            Assert.That(snapshotReads, Is.EqualTo(1));
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Context, Does.Contain(Hash(BaseXml)));
        }

        [Test]
        public void Editor_restore_rebases_unrelated_edits_and_deletes_pending_only_after_apply()
        {
            var pending = CreatePending(BaseXml, AddVariablePatch(BaseXml), "craft-alpha");
            var current = BaseXml.Replace("before", "unrelated");
            var deletes = 0;
            var environment = EditorEnvironment(
                (_, __) => Task.FromResult<PendingChange?>(pending),
                (_, __) => { deletes++; return Task.CompletedTask; });
            var adapter = new FakeAdapter(current, true, false);
            using var workflow = CreateWorkflow(adapter, environment, (_, __) => throw new AssertionException("No request expected."));

            workflow.OpenPanel();
            workflow.RestorePendingAsync().GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
            Assert.That(workflow.Mode, Is.EqualTo(VizzyGptPanelMode.Modify));
            Assert.That(workflow.StatusText, Does.Contain("rebased"));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
            Assert.That(deletes, Is.EqualTo(0));
            workflow.SetMode(VizzyGptPanelMode.Modify);
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(adapter.SetCalls, Is.EqualTo(1));
            Assert.That(deletes, Is.EqualTo(1));
        }

        [Test]
        public void Editor_restore_deletes_the_loaded_pending_records_original_fingerprint()
        {
            var pending = CreatePending(BaseXml, AddVariablePatch(BaseXml), "program-flight-hash");
            string? deletedFingerprint = null;
            var environment = EditorEnvironment(
                (_, __) => Task.FromResult<PendingChange?>(pending),
                (fingerprint, _) =>
                {
                    deletedFingerprint = fingerprint;
                    return Task.CompletedTask;
                });
            using var workflow = CreateWorkflow(
                new FakeAdapter(BaseXml, true, false),
                environment,
                (_, __) => throw new AssertionException("No request expected."));

            workflow.OpenPanel();
            workflow.RestorePendingAsync().GetAwaiter().GetResult();
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);

            Assert.That(deletedFingerprint, Is.EqualTo("program-flight-hash"));
        }

        [Test]
        public void Editor_restore_conflict_never_mutates_and_discard_deletes_the_pending_record()
        {
            var update = new PatchDocument(
                Hash(BaseXml),
                "Change log text",
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.UpdateAttribute,
                        target: new NodeSelector(1, null),
                        attribute: "text",
                        value: "after")
                });
            var pending = CreatePending(BaseXml, update, "craft-alpha");
            var deletes = 0;
            var adapter = new FakeAdapter(BaseXml.Replace("before", "manual"), true, false);
            using var workflow = CreateWorkflow(
                adapter,
                EditorEnvironment(
                    (_, __) => Task.FromResult<PendingChange?>(pending),
                    (_, __) => { deletes++; return Task.CompletedTask; }),
                (_, __) => throw new AssertionException("No request expected."));

            workflow.OpenPanel();
            workflow.RestorePendingAsync().GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(workflow.StatusText, Does.Contain("conflict"));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
            workflow.DiscardPendingAsync().GetAwaiter().GetResult();
            Assert.That(deletes, Is.EqualTo(1));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        private static VizzyGptPanelWorkflow CreateWorkflow(
            FakeAdapter adapter,
            VizzyGptWorkflowEnvironment environment,
            Func<AiRequest, CancellationToken, Task<AiResponse>> send)
        {
            return new VizzyGptPanelWorkflow(
                adapter,
                send,
                (prompt, context) => new AiRequest(
                    ApiMode.Auto,
                    prompt,
                    context,
                    "test",
                    new Uri("https://api.example.test"),
                    "key",
                    TimeSpan.FromSeconds(10)),
                (_, __, ___, ____) => Task.CompletedTask,
                () => VizzyNodeCatalog.FromToolboxXml(
                    "<VizzyToolbox><Instructions><Log /></Instructions><Expressions /></VizzyToolbox>"),
                () => Now,
                _ => { },
                environment);
        }

        private static VizzyGptWorkflowEnvironment FlightEnvironment(
            Func<string, CancellationToken, Task<PendingChange?>> load,
            Func<PendingChange, CancellationToken, Task> save)
        {
            return new VizzyGptWorkflowEnvironment(
                true,
                "craft-alpha",
                BaseXml,
                () => "FLIGHT CONTEXT\nTelemetry summaries:",
                load,
                save,
                (_, __) => Task.CompletedTask);
        }

        private static VizzyGptWorkflowEnvironment EditorEnvironment(
            Func<string, CancellationToken, Task<PendingChange?>> load,
            Func<string, CancellationToken, Task> delete)
        {
            return new VizzyGptWorkflowEnvironment(
                false,
                "craft-alpha",
                (string?)null,
                () => string.Empty,
                load,
                (_, __) => Task.CompletedTask,
                delete);
        }

        private static AiResponse ResponseFor(string xml)
        {
            return new AiResponse("preview", AddVariablePatch(xml), true, Array.Empty<string>());
        }

        private static PatchDocument AddVariablePatch(string xml)
        {
            return new PatchDocument(
                Hash(xml),
                "Add counter",
                new[] { new PatchOperation(PatchOperationType.AddVariable, name: "counter", value: "0") });
        }

        private static PendingChange CreatePending(string xml, PatchDocument patch, string fingerprint)
        {
            var document = VizzyProgramDocument.Parse(xml);
            var result = VizzyPatchEngine.Apply(document, patch);
            var report = new ValidationReport(Array.Empty<ValidationIssue>());
            return PendingChange.Create(fingerprint, ChangeSession.Create(document, patch, result, report), Now);
        }

        private static string Hash(string xml)
        {
            return VizzyProgramHash.Compute(VizzyProgramDocument.Parse(xml));
        }

        private sealed class FakeAdapter : IVizzyRuntimeAdapter
        {
            private readonly bool editorAvailable;
            private readonly bool flightAvailable;

            public FakeAdapter(string xml, bool editorAvailable, bool flightAvailable)
            {
                Xml = xml;
                this.editorAvailable = editorAvailable;
                this.flightAvailable = flightAvailable;
            }

            public string Xml { get; private set; }

            public int SetCalls { get; private set; }

            public bool IsEditorAvailable => editorAvailable;

            public bool IsFlightAvailable => flightAvailable;

            public bool TryGetEditorProgramXml(out string xml, out string error)
            {
                xml = Xml;
                error = editorAvailable ? string.Empty : "Editor unavailable.";
                return editorAvailable;
            }

            public bool TrySetEditorProgramXml(string xml, out string error)
            {
                SetCalls++;
                Xml = xml;
                error = string.Empty;
                return true;
            }

            public bool TryGetFlightProgramXml(out string xml, out string error)
            {
                xml = Xml;
                error = flightAvailable ? string.Empty : "Flight unavailable.";
                return flightAvailable;
            }

            public ValidationIssue? ValidateWithProgramSerializer(string xml)
            {
                return null;
            }
        }
    }
}
