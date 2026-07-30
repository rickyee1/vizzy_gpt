#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Conversations;
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
        public void Modify_repairs_one_protected_root_failure_against_the_original_source()
        {
            var requests = new List<AiRequest>();
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1
                    ? CreateProtectedRootResponse(InitialXml, "failed-output-marker")
                    : CreateValidModifyResponseValue(InitialXml));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            var originalHash = VizzyProgramHash.Compute(VizzyProgramDocument.Parse(InitialXml));
            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[1].Context, Does.Contain("ProtectedRoot"));
            Assert.That(requests[1].Context, Does.Contain(originalHash));
            Assert.That(requests[1].Context, Does.Contain("before"));
            Assert.That(requests[1].Context, Does.Not.Contain("failed-output-marker"));
            Assert.That(adapter.SetCalls, Is.Zero);
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
        }

        [Test]
        public void Modify_stops_after_one_failed_repair_with_terminal_diagnostics()
        {
            var sendCount = 0;
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, (_, __) =>
            {
                sendCount++;
                return Task.FromResult(CreateProtectedRootResponse(InitialXml, "invalid-" + sendCount));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Remove the instruction root.").GetAwaiter().GetResult();

            Assert.That(sendCount, Is.EqualTo(2));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(workflow.CurrentRenderState.Entries.Last().Error, Is.Not.Null);
            Assert.That(workflow.CurrentRenderState.Entries.Last().Error!.TechnicalDetails, Does.Contain("protected"));
            Assert.That(adapter.SetCalls, Is.Zero);
        }

        [Test]
        public void Modify_does_not_retry_after_client_schema_repair_is_exhausted()
        {
            var sendCount = 0;
            using var workflow = CreateWorkflow(new FakeAdapter(InitialXml), (_, __) =>
            {
                sendCount++;
                return Task.FromResult(new AiResponse(
                    "The model response could not be validated.",
                    null,
                    false,
                    new[] { "Invalid patch envelope." },
                    new AiResponseMetadata(null, null, null, true)));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
        }

        [Test]
        public void Modify_disables_client_schema_repair_on_the_workflow_repair_request()
        {
            var requests = new List<AiRequest>();
            using var workflow = CreateWorkflow(new FakeAdapter(InitialXml), (request, _) =>
            {
                requests.Add(request);
                return Task.FromResult(requests.Count == 1
                    ? CreateProtectedRootResponse(InitialXml, "invalid")
                    : CreateValidModifyResponseValue(InitialXml));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(requests, Has.Count.EqualTo(2));
            Assert.That(requests[0].AllowSchemaRepair, Is.True);
            Assert.That(requests[1].AllowSchemaRepair, Is.False);
        }

        [Test]
        public void Modify_records_ordered_stages_and_provider_reasoning()
        {
            var now = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, __) =>
                {
                    now = now.AddSeconds(2);
                    var response = CreateValidModifyResponseValue(InitialXml);
                    return Task.FromResult(new AiResponse(
                        response.Message,
                        response.Patch,
                        response.CanApply,
                        response.Diagnostics,
                        new AiResponseMetadata("Checked the patch.", null, null, false)));
                },
                utcNow: () => now);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            var assistant = workflow.CurrentRenderState.Entries.Last();
            Assert.That(assistant.ReasoningSummary, Is.EqualTo("Checked the patch."));
            Assert.That(
                assistant.Stages.Select(stage => stage.Stage),
                Is.EqualTo(new[]
                {
                    "ReadingProgram",
                    "BuildingContext",
                    "WaitingForModel",
                    "ParsingPatch",
                    "ValidatingPatch",
                    "PreparingPreview"
                }));
            Assert.That(assistant.CanPreview, Is.True);
        }

        [Test]
        public void Refresh_elapsed_rerenders_only_while_sending()
        {
            var now = new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc);
            var renders = 0;
            using var started = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) =>
                {
                    started.Set();
                    return completion.Task;
                },
                render: _ => renders++,
                utcNow: () => now);

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("Explain.");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);
            var beforeRefresh = renders;
            now = now.AddSeconds(1.5);

            workflow.RefreshElapsed();

            Assert.That(renders, Is.EqualTo(beforeRefresh + 1));
            Assert.That(workflow.CurrentRenderState.Entries.Last().ElapsedSeconds, Is.EqualTo(1.5).Within(0.001));
            completion.SetResult(new AiResponse("Done.", null, false, Array.Empty<string>()));
            sending.GetAwaiter().GetResult();
            beforeRefresh = renders;

            workflow.RefreshElapsed();

            Assert.That(renders, Is.EqualTo(beforeRefresh));
        }

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
            Assert.That(sentRequest!.Purpose, Is.EqualTo(AiRequestPurpose.Ask));
            Assert.That(sentRequest.Context, Does.Contain("<Log id=\"1\" text=\"before\""));
            Assert.That(sentRequest.Context, Does.Contain("Ask mode does not permit program mutation."));
            Assert.That(workflow.CanApply, Is.False);
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Modify_marks_the_model_request_as_a_patch_request()
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

            Assert.That(sentRequest, Is.Not.Null);
            Assert.That(sentRequest!.Purpose, Is.EqualTo(AiRequestPurpose.Modify));
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
            var sendCount = 0;
            using var requestStarted = new ManualResetEventSlim();
            var completion = new TaskCompletionSource<AiResponse>();
            var observedToken = default(CancellationToken);
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, cancellationToken) =>
                {
                    sendCount++;
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
            Assert.That(sendCount, Is.EqualTo(1));
        }

        [Test]
        public void Modify_transport_failure_does_not_trigger_repair()
        {
            var sendCount = 0;
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, (_, __) =>
            {
                sendCount++;
                throw new InvalidOperationException("transport unavailable");
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(adapter.SetCalls, Is.Zero);
        }

        [Test]
        public void Modify_catalog_infrastructure_failure_does_not_trigger_repair()
        {
            var sendCount = 0;
            var catalogCalls = 0;
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) =>
                {
                    sendCount++;
                    return Task.FromResult(CreateValidModifyResponseValue(InitialXml));
                },
                createCatalog: () =>
                {
                    catalogCalls++;
                    if (catalogCalls == 2)
                    {
                        throw new InvalidOperationException("catalog unavailable");
                    }

                    return CreateCatalog();
                });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(workflow.CurrentRenderState.Entries.Last().Error!.Code,
                Is.EqualTo(nameof(InvalidOperationException)));
        }

        [Test]
        public void Modify_serializer_infrastructure_failure_does_not_trigger_repair()
        {
            var sendCount = 0;
            var adapter = new FakeAdapter(
                InitialXml,
                (validationCall, _) => validationCall == 2
                    ? throw new InvalidOperationException("serializer unavailable")
                    : null);
            using var workflow = CreateWorkflow(adapter, (_, __) =>
            {
                sendCount++;
                return Task.FromResult(CreateValidModifyResponseValue(InitialXml));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(sendCount, Is.EqualTo(1));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(adapter.SetCalls, Is.Zero);
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
            completion.TrySetResult(new AiResponse("Ignored.", null, false, Array.Empty<string>()));
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Closed));
            Assert.That(workflow.CurrentRenderState.Entries.Any(entry => entry.CurrentStage.HasValue), Is.False);

            workflow.OpenPanel();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
            Assert.That(workflow.CurrentRenderState.Entries.Any(entry => entry.CurrentStage.HasValue), Is.False);
        }

        [Test]
        public void Modify_cancellation_at_repair_boundary_prevents_second_transport()
        {
            var sendCount = 0;
            VizzyGptPanelWorkflow? workflow = null;
            var adapter = new FakeAdapter(
                InitialXml,
                (validationCall, _) =>
                {
                    if (validationCall != 2)
                    {
                        return null;
                    }

                    workflow!.CancelRequest();
                    return new ValidationIssue(
                        ValidationSeverity.Error,
                        "CatalogPlacement",
                        "Model patch placement is invalid.",
                        "/Program[0]/Instructions[0]");
                });
            workflow = CreateWorkflow(adapter, (_, __) =>
            {
                sendCount++;
                return Task.FromResult(CreateValidModifyResponseValue(InitialXml));
            });
            using (workflow)
            {
                workflow.OpenPanel();
                workflow.SetMode(VizzyGptPanelMode.Modify);
                workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

                Assert.That(sendCount, Is.EqualTo(1));
                Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
                Assert.That(adapter.SetCalls, Is.Zero);
            }
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
        public void Modify_rejects_an_invalid_source_program_before_transport()
        {
            var sendCalls = 0;
            var invalidSource = "<Program><Variables /><Expressions /></Program>";
            var adapter = new FakeAdapter(invalidSource);
            using var workflow = CreateWorkflow(adapter, (_, __) =>
            {
                sendCalls++;
                return Task.FromResult(CreateValidModifyResponseValue(invalidSource));
            });

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(sendCalls, Is.EqualTo(0));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Error));
            Assert.That(workflow.StatusText, Does.Contain("Current Vizzy program is not safe to modify"));
            Assert.That(adapter.SetCalls, Is.EqualTo(0));
        }

        [Test]
        public void Modify_rejects_response_when_editor_changed_while_request_was_pending()
        {
            var sendCount = 0;
            using var requestStarted = new ManualResetEventSlim();
            var response = new TaskCompletionSource<AiResponse>();
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(
                adapter,
                (_, __) =>
                {
                    sendCount++;
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
            Assert.That(sendCount, Is.EqualTo(1));
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
            Action<VizzyGptPanelRenderState>? render = null,
            Func<DateTime>? utcNow = null,
            IConversationStore? conversationStore = null,
            Func<VizzyNodeCatalog>? createCatalog = null)
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
                createCatalog ?? CreateCatalog,
                utcNow ?? (() => new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc)),
                render ?? (_ => { }),
                conversationStore: conversationStore);
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

        private static AiResponse CreateProtectedRootResponse(string baseXml, string marker)
        {
            var document = VizzyProgramDocument.Parse(baseXml);
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                marker,
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.RemoveNode,
                        new NodeSelector(null, "/Program[0]/Instructions[0]"))
                });
            return new AiResponse(marker, patch, true, Array.Empty<string>());
        }

        private static VizzyNodeCatalog CreateCatalog()
        {
            return VizzyNodeCatalog.FromToolboxXml(
                "<VizzyToolbox><Instructions><Log /></Instructions><Expressions /></VizzyToolbox>");
        }

        private sealed class FakeAdapter : IVizzyRuntimeAdapter
        {
            private readonly Func<int, string, ValidationIssue?>? validate;
            private int validationCalls;

            public FakeAdapter(
                string xml,
                Func<int, string, ValidationIssue?>? validate = null)
            {
                Xml = xml;
                this.validate = validate;
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
                validationCalls++;
                return validate?.Invoke(validationCalls, xml);
            }
        }
    }
}
