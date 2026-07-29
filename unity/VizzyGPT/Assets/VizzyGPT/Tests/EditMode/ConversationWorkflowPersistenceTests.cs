#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
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
    public sealed class ConversationWorkflowPersistenceTests
    {
        private const string InitialXml =
            "<Program><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>";
        private static readonly DateTime Now =
            new DateTime(2026, 7, 29, 1, 0, 0, DateTimeKind.Utc);

        [Test]
        public void Setup_load_renders_existing_general_history_without_saving()
        {
            var old = Message("old", ConversationRole.Assistant, "Earlier response.");
            var store = new FakeConversationStore(new ConversationHistory(1, "general", new[] { old }));
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) => Task.FromResult(new AiResponse("Unused.", null, false, Array.Empty<string>())),
                store);

            workflow.LoadConversationAsync().GetAwaiter().GetResult();

            Assert.That(workflow.CurrentRenderState.Entries.Select(entry => entry.Text),
                Does.Contain("Earlier response."));
            Assert.That(store.Saved, Is.Empty);
        }

        [Test]
        public void Clear_history_removes_only_the_loaded_conversation_and_renders_empty()
        {
            var old = Message("old", ConversationRole.Assistant, "Earlier response.");
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", new[] { old }));
            var renderCount = 0;
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) => Task.FromResult(new AiResponse("Unused.", null, false, Array.Empty<string>())),
                store,
                _ => renderCount++);
            workflow.LoadConversationAsync().GetAwaiter().GetResult();
            var rendersBeforeClear = renderCount;

            workflow.ClearConversationAsync(CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(store.Cleared, Is.EqualTo(new[] { "conversation-a" }));
            Assert.That(workflow.CurrentRenderState.Entries, Is.Empty);
            Assert.That(renderCount, Is.EqualTo(rendersBeforeClear + 1));
        }

        [Test]
        public void Terminal_response_restores_and_saves_the_active_history()
        {
            var old = Message("old", ConversationRole.Assistant, "Earlier response.");
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", new[] { old }));
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) => Task.FromResult(new AiResponse("Current response.", null, false, Array.Empty<string>())),
                store);

            workflow.OpenPanel();
            workflow.SendPromptAsync("Explain.").GetAwaiter().GetResult();

            Assert.That(workflow.CurrentRenderState.Entries.Select(entry => entry.Text),
                Does.Contain("Earlier response."));
            Assert.That(store.Saved, Has.Count.EqualTo(1));
            Assert.That(store.Saved[0].Messages.Select(message => message.Text),
                Does.Contain("Current response."));
        }

        [Test]
        public void Elapsed_refresh_never_writes_conversation_history()
        {
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", Array.Empty<ConversationMessage>()));
            var completion = new TaskCompletionSource<AiResponse>();
            using var started = new ManualResetEventSlim();
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) =>
                {
                    started.Set();
                    return completion.Task;
                },
                store);

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("Explain.");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);

            workflow.RefreshElapsed();
            workflow.RefreshElapsed();

            Assert.That(store.Saved, Is.Empty);
            completion.SetResult(new AiResponse("Done.", null, false, Array.Empty<string>()));
            sending.GetAwaiter().GetResult();
            Assert.That(store.Saved, Has.Count.EqualTo(1));
        }

        [Test]
        public void Close_during_request_persists_cancelled_terminal_without_reopening()
        {
            Task.Run(async () =>
            {
                var root = Path.Combine(
                    Path.GetTempPath(),
                    "VizzyGPT-CloseConversation-" + Guid.NewGuid().ToString("N"));
                try
                {
                    var store = new FileConversationStore(root);
                    var completion = new TaskCompletionSource<AiResponse>();
                    using var started = new ManualResetEventSlim();
                    using var workflow = CreateWorkflow(
                        new FakeAdapter(InitialXml),
                        (_, __) =>
                        {
                            started.Set();
                            return completion.Task;
                        },
                        store);

                    await workflow.LoadConversationAsync();
                    workflow.OpenPanel();
                    var sending = workflow.SendPromptAsync("Explain.");
                    Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);

                    workflow.ClosePanel();
                    completion.SetResult(new AiResponse(
                        "Ignored.",
                        null,
                        false,
                        Array.Empty<string>()));
                    await sending;

                    var loaded = await store.LoadOrCreateAsync(null);
                    for (var attempt = 0;
                         attempt < 100 &&
                         !loaded.Messages.Any(message => message.Kind == ConversationMessageKind.Cancelled);
                         attempt++)
                    {
                        await Task.Delay(10);
                        loaded = await new FileConversationStore(root).LoadOrCreateAsync(null);
                    }

                    Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Closed));
                    Assert.That(loaded.Messages.Any(
                        message => message.Kind == ConversationMessageKind.Cancelled), Is.True);
                    Assert.That(loaded.Messages.Any(
                        message => message.Kind == ConversationMessageKind.Progress), Is.False);

                    workflow.OpenPanel();
                    Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
                    Assert.That(workflow.CurrentRenderState.Entries.Any(
                        entry => entry.CurrentStage != null), Is.False);
                }
                finally
                {
                    if (Directory.Exists(root))
                    {
                        Directory.Delete(root, true);
                    }
                }
            }).GetAwaiter().GetResult();
        }

        [Test]
        public void Close_persistence_failure_stays_closed_and_surfaces_warning()
        {
            var store = new FakeConversationStore(
                new ConversationHistory(1, "conversation-a", Array.Empty<ConversationMessage>()))
            {
                ThrowOnSave = true
            };
            var completion = new TaskCompletionSource<AiResponse>();
            using var started = new ManualResetEventSlim();
            using var workflow = CreateWorkflow(
                new FakeAdapter(InitialXml),
                (_, __) =>
                {
                    started.Set();
                    return completion.Task;
                },
                store);

            workflow.OpenPanel();
            var sending = workflow.SendPromptAsync("Explain.");
            Assert.That(started.Wait(TimeSpan.FromSeconds(1)), Is.True);

            workflow.ClosePanel();
            completion.SetResult(new AiResponse("Ignored.", null, false, Array.Empty<string>()));
            sending.GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Closed));
            Assert.That(workflow.CurrentRenderState.Entries.Last().Error!.Code,
                Is.EqualTo("PersistenceWarning"));
            Assert.That(workflow.CurrentRenderState.Entries.Any(
                entry => entry.CurrentStage != null), Is.False);
        }

        [Test]
        public void Apply_links_the_result_hash_without_disabling_apply_when_persistence_fails()
        {
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", Array.Empty<ConversationMessage>()));
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, ValidModifyResponse(InitialXml), store);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();
            store.ThrowOnSave = true;

            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);

            var resultHash = VizzyProgramHash.Compute(VizzyProgramDocument.Parse(adapter.Xml));
            Assert.That(store.Linked, Does.Contain(("conversation-a", resultHash)));
            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
            Assert.That(adapter.SetCalls, Is.EqualTo(1));
            Assert.That(workflow.CurrentRenderState.Entries.Last().Error!.Code, Is.EqualTo("PersistenceWarning"));
        }

        [Test]
        public void Persistence_warning_after_modify_does_not_hide_preview()
        {
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", Array.Empty<ConversationMessage>()))
            {
                ThrowOnSave = true
            };
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, ValidModifyResponse(InitialXml), store);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();

            Assert.That(workflow.State, Is.EqualTo(VizzyGptPanelState.PreviewReady));
            Assert.That(workflow.CanApply, Is.True);
            Assert.That(workflow.CurrentRenderState.Entries.Count(entry => entry.CanPreview), Is.EqualTo(1));
            Assert.That(adapter.SetCalls, Is.Zero);
        }

        [Test]
        public void Undo_links_the_restored_base_hash()
        {
            var store = new FakeConversationStore(new ConversationHistory(1, "conversation-a", Array.Empty<ConversationMessage>()));
            var adapter = new FakeAdapter(InitialXml);
            using var workflow = CreateWorkflow(adapter, ValidModifyResponse(InitialXml), store);

            workflow.OpenPanel();
            workflow.SetMode(VizzyGptPanelMode.Modify);
            workflow.SendPromptAsync("Add a counter.").GetAwaiter().GetResult();
            Assert.That(workflow.ApplySessionAsync().GetAwaiter().GetResult(), Is.True);

            Assert.That(workflow.UndoLastAsync().GetAwaiter().GetResult(), Is.True);

            var baseHash = VizzyProgramHash.Compute(VizzyProgramDocument.Parse(InitialXml));
            Assert.That(store.Linked.Last(), Is.EqualTo(("conversation-a", baseHash)));
        }

        private static VizzyGptPanelWorkflow CreateWorkflow(
            FakeAdapter adapter,
            Func<AiRequest, CancellationToken, Task<AiResponse>> send,
            IConversationStore store,
            Action<VizzyGptPanelRenderState>? render = null)
        {
            return new VizzyGptPanelWorkflow(
                adapter,
                send,
                (prompt, context) => new AiRequest(
                    ApiMode.Auto,
                    prompt,
                    context,
                    "test-model",
                    new Uri("https://api.example.test"),
                    "test-key",
                    TimeSpan.FromSeconds(10)),
                (_, __, ___, ____) => Task.CompletedTask,
                () => VizzyNodeCatalog.FromToolboxXml(
                    "<VizzyToolbox><Instructions><Log /></Instructions><Expressions /></VizzyToolbox>"),
                () => Now,
                render ?? (_ => { }),
                conversationStore: store);
        }

        private static Func<AiRequest, CancellationToken, Task<AiResponse>> ValidModifyResponse(string xml)
        {
            return (_, __) =>
            {
                var document = VizzyProgramDocument.Parse(xml);
                return Task.FromResult(new AiResponse(
                    "Preview.",
                    new PatchDocument(
                        VizzyProgramHash.Compute(document),
                        "Add counter",
                        new[] { new PatchOperation(PatchOperationType.AddVariable, name: "counter", value: "0") }),
                    true,
                    Array.Empty<string>()));
            };
        }

        private static ConversationMessage Message(string id, ConversationRole role, string text)
        {
            return new ConversationMessage(
                id,
                role,
                ConversationMessageKind.Message,
                ConversationMode.Ask,
                text,
                null,
                Array.Empty<ConversationStageTiming>(),
                null,
                null,
                Now);
        }

        private sealed class FakeConversationStore : IConversationStore
        {
            private readonly ConversationHistory loaded;

            public FakeConversationStore(ConversationHistory loaded)
            {
                this.loaded = loaded;
            }

            public bool ThrowOnSave { get; set; }
            public List<ConversationHistory> Saved { get; } = new List<ConversationHistory>();
            public List<(string ConversationId, string ProgramHash)> Linked { get; } =
                new List<(string ConversationId, string ProgramHash)>();
            public List<string> Cleared { get; } = new List<string>();

            public Task<ConversationHistory> LoadOrCreateAsync(
                string? programHash,
                CancellationToken cancellationToken = default)
            {
                return Task.FromResult(loaded);
            }

            public Task SaveAsync(
                ConversationHistory history,
                CancellationToken cancellationToken = default)
            {
                if (ThrowOnSave)
                {
                    throw new InvalidOperationException("disk request body: secret");
                }

                Saved.Add(history);
                return Task.CompletedTask;
            }

            public Task LinkProgramHashAsync(
                string conversationId,
                string programHash,
                CancellationToken cancellationToken = default)
            {
                Linked.Add((conversationId, programHash));
                return Task.CompletedTask;
            }

            public Task ClearAsync(
                string conversationId,
                CancellationToken cancellationToken = default)
            {
                Cleared.Add(conversationId);
                return Task.CompletedTask;
            }
        }

        private sealed class FakeAdapter : IVizzyRuntimeAdapter
        {
            public FakeAdapter(string xml)
            {
                Xml = xml;
            }

            public string Xml { get; private set; }
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
                Xml = xml;
                SetCalls++;
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
