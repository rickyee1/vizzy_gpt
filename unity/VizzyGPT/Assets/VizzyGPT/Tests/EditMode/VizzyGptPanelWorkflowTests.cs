#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyGptPanelWorkflowTests
    {
        private const string InitialXml =
            "<Program><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>";

        [Test]
        public void Ask_escapes_output_and_never_enables_apply()
        {
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, __) => Task.FromResult(new AiResponse("<assistant>&", null, false, Array.Empty<string>())));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Ask);
            workflow.SendPromptAsync("Explain this program.").GetAwaiter().GetResult();

            Assert.That(workflow.TranscriptText, Does.Contain("&lt;assistant&gt;&amp;"));
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Editor_ask_includes_the_active_program_without_enabling_mutation()
        {
            AiRequest? sentRequest = null;
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, (request, _) =>
            {
                sentRequest = request;
                return Task.FromResult(new AiResponse("Explanation.", null, false, Array.Empty<string>()));
            });

            workflow.OpenPanel();
            workflow.SendPromptAsync("Explain this program.").GetAwaiter().GetResult();

            Assert.That(sentRequest, Is.Not.Null);
            Assert.That(sentRequest!.Context, Does.Contain("<Log id=\"1\" text=\"before\""));
            Assert.That(sentRequest.Context, Does.Contain("Ask mode does not permit program mutation."));
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Render_state_centrally_controls_pending_actions()
        {
            VizzyGptPanelRenderState? rendered = null;
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                CreateValidModifyResponse(InitialXml),
                render: value => rendered = value);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(rendered, Is.Not.Null);
            Assert.That(rendered!.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
            Assert.That(rendered.CanPreview, Is.True);
            Assert.That(rendered.CanSend, Is.True);
            Assert.That(rendered.CanCancel, Is.False);
        }

        [Test]
        public void Cancel_aborts_the_in_flight_request()
        {
            using var requestStarted = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            var observedToken = default(CancellationToken);
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, cancellationToken) =>
                {
                    observedToken = cancellationToken;
                    requestStarted.Set();
                    return completion.Task;
                });

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("Cancel this request.");
            Assert.That(requestStarted.Wait(TimeSpan.FromSeconds(1)), Is.True);
            workflow.CancelRequest();
            Assert.That(observedToken.IsCancellationRequested, Is.True);
            completion.TrySetCanceled(observedToken);
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
        }

        [Test]
        public void Close_during_request_keeps_panel_closed_when_cancellation_continues()
        {
            using var started = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            using var workflow = CreateWorkflow(new FakeAdapter(InitialXml), (_, __) =>
            {
                started.Set();
                return completion.Task;
            });

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("cancel");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);
            workflow.ClosePanel();
            completion.TrySetCanceled();
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Closed));
        }

        [Test]
        public void Mode_change_during_request_is_ignored_and_response_uses_captured_mode()
        {
            using var started = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            using var workflow = CreateWorkflow(new FakeAdapter(InitialXml), (_, __) =>
            {
                started.Set();
                return completion.Task;
            });

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("ask");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);
            workflow.SetMode(VizzyGptPanelMode.Modify);
            completion.TrySetResult(CreateValidModifyResponseValue(InitialXml));
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.Mode, Is.EqualTo(VizzyGptPanelMode.Ask));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
            Assert.That(workflow.CanApply, Is.False);
        }

        [Test]
        public void Modify_to_ask_mode_change_during_request_is_ignored()
        {
            using var started = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            using var workflow = CreateWorkflow(new FakeAdapter(InitialXml), (_, __) =>
            {
                started.Set();
                return completion.Task;
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            var sending = workflow.SendPromptAsync("modify");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);
            workflow.SetMode(VizzyGptPanelMode.Ask);
            completion.TrySetResult(CreateValidModifyResponseValue(InitialXml));
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.Mode, Is.EqualTo(VizzyGptPanelMode.Modify));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
        }

        [Test]
        public void Modify_creates_preview_without_mutating_the_editor()
        {
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, CreateValidModifyResponse(InitialXml));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
            Assert.That(workflow.ShowPreview(), Is.Not.Null);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Modify_sends_the_current_program_hash_in_the_model_context()
        {
            AiRequest? sentRequest = null;
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, (request, _) =>
            {
                sentRequest = request;
                return Task.FromResult(CreateValidModifyResponseValue(InitialXml));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            var expectedHash = VizzyProgramHash.Compute(VizzyProgramDocument.Parse(InitialXml));
            Assert.That(sentRequest, Is.Not.Null);
            Assert.That(sentRequest!.Context, Does.Contain("Program base hash:\n" + expectedHash));
        }

        [Test]
        public void Modify_rejects_response_when_editor_changed_while_request_was_pending()
        {
            using var requestStarted = new ManualResetEventSlim();
            var response = new TaskCompletionSource<AiResponse>();
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, __) =>
                {
                    requestStarted.Set();
                    return response.Task;
                });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            var sending = workflow.SendPromptAsync("Add a counter.");
            Assert.That(requestStarted.Wait(TimeSpan.FromSeconds(1)), Is.True);
            adapter.Xml = InitialXml.Replace("before", "changed");
            response.TrySetResult(CreateValidModifyResponseValue(InitialXml));
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(workflow.StatusText, Does.Contain("changed while the request was in progress"));
            Assert.That(workflow.ShowPreview(), Is.Null);
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Modify_without_a_valid_session_never_enables_apply()
        {
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, __) => Task.FromResult(new AiResponse("No patch.", null, false, Array.Empty<string>())));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Do not change anything.").GetAwaiter().GetResult();

            Assert.That(workflow.CanApply, Is.False);
            Assert.That(workflow.ShowPreview(), Is.Null);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Stale_base_hash_disables_apply_before_backup_or_mutation()
        {
            var adapter = new FakeAdapter(InitialXml);
            var backupCalls = 0;
            using var workflow = CreateWorkflow(
                adapter,
                CreateValidModifyResponse(InitialXml),
                (_, __, ___, ____) =>
                {
                    backupCalls++;
                    return Task.CompletedTask;
                });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();
            adapter.Xml = InitialXml.Replace("before", "changed");

            Assert.That(workflow.ShowPreview(), Is.Null);
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(backupCalls, Is.EqualTo(0));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Failed_backup_disables_apply_without_mutating_the_editor()
        {
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                CreateValidModifyResponse(InitialXml),
                (_, __, ___, ____) => throw new InvalidOperationException("disk unavailable"));

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Apply_writes_once_and_undo_restores_the_validated_backup()
        {
            var adapter = new FakeAdapter(InitialXml);
            var savedBackups = new List<string>();
            using var workflow = CreateWorkflow(
                adapter,
                CreateValidModifyResponse(InitialXml),
                (_, xml, __, ___) =>
                {
                    savedBackups.Add(xml);
                    return Task.CompletedTask;
                });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(adapter.SetCalls, Is.EqualTo(1));
            Assert.That(adapter.Xml, Does.Contain("Variable name=\"counter\""));

            Assert.That(workflow.UndoLastAsync().GetAwaiter().GetResult(), Is.True);
            Assert.That(adapter.SetCalls, Is.EqualTo(2));
            Assert.That(adapter.Xml, Is.EqualTo(InitialXml));
            Assert.That(savedBackups, Has.Count.EqualTo(2));
            Assert.That(savedBackups[0], Is.EqualTo(InitialXml));
            Assert.That(savedBackups[1], Does.Contain("Variable name=\"counter\""));
        }

        [Test]
        public void Undo_rejects_unrelated_edits_without_backup_or_mutation()
        {
            var adapter = new FakeAdapter(InitialXml);
            var backups = 0;
            using var workflow = CreateWorkflow(adapter, CreateValidModifyResponse(InitialXml), (_, __, ___, ____) =>
            {
                backups++;
                return Task.CompletedTask;
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("add").GetAwaiter().GetResult();
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);
            adapter.Xml = adapter.Xml.Replace("counter", "other");

            Assert.That(workflow.UndoLastAsync().GetAwaiter().GetResult(), Is.False);
            Assert.That(backups, Is.EqualTo(1));
            Assert.That(adapter.SetCalls, Is.EqualTo(1));
        }

        private static VizzyGptPanelWorkflow CreateWorkflow(
            FakeAdapter adapter,
            Func<AiRequest, CancellationToken, Task<AiResponse>> sendAsync,
            Func<string, string, DateTime, CancellationToken, Task>? saveBackupAsync = null,
            Action<VizzyGptPanelRenderState>? render = null)
        {
            return new VizzyGptPanelWorkflow(
                adapter,
                sendAsync,
                (prompt, context) => new AiRequest(
                    ApiMode.Auto,
                    prompt,
                    context,
                    "test-model",
                    new Uri("https://api.example.test"),
                    "test-key",
                    TimeSpan.FromSeconds(10)),
                saveBackupAsync ?? ((_, __, ___, ____) => Task.CompletedTask),
                CreateCatalog,
                () => new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc),
                render ?? (_ => { }));
        }

        private static Func<AiRequest, CancellationToken, Task<AiResponse>> CreateValidModifyResponse(string baseXml)
        {
            return (_, __) => Task.FromResult(CreateValidModifyResponseValue(baseXml));
        }

        private static AiResponse CreateValidModifyResponseValue(string baseXml)
        {
            var document = VizzyProgramDocument.Parse(baseXml);
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Add counter",
                new[]
                {
                    new PatchOperation(PatchOperationType.AddVariable, name: "counter", value: "0")
                });
            return new AiResponse("Counter preview.", patch, true, Array.Empty<string>());
        }

        private static VizzyNodeCatalog CreateCatalog()
        {
            return VizzyNodeCatalog.FromToolboxXml(
                "<VizzyToolbox><Instructions><Log /></Instructions><Expressions /></VizzyToolbox>");
        }

        private sealed class FakeAdapter : IVizzyRuntimeAdapter
        {
            public FakeAdapter(string xml)
            {
                Xml = xml;
            }

            public string Xml { get; set; }

            public int SetCalls { get; private set; }

            public bool IsEditorAvailable => true;

            public bool IsFlightAvailable => false;

            public bool TryGetEditorProgramXml(out string xml, out string error)
            {
                xml = Xml;
                error = string.Empty;
                return true;
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
                xml = string.Empty;
                error = "Unavailable.";
                return false;
            }

            public ValidationIssue? ValidateWithProgramSerializer(string xml)
            {
                return null;
            }
        }
    }
}
