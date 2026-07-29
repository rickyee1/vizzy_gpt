using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Conversations;

namespace VizzyGPT.Core.Tests.Conversations
{
    public sealed class FileConversationStoreTests
    {
        [Test]
        public void Persistent_records_reject_invalid_constructor_values()
        {
            var utc = new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc);

            Assert.That(
                () => CreateMessage(text: null!, createdUtc: utc),
                Throws.TypeOf<ArgumentNullException>());
            Assert.That(
                () => CreateMessage(createdUtc: DateTime.SpecifyKind(utc, DateTimeKind.Local)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => CreateMessage(role: (ConversationRole)99, createdUtc: utc),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateMessage(kind: (ConversationMessageKind)99, createdUtc: utc),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateMessage(mode: (ConversationMode)99, createdUtc: utc),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => CreateMessage(elapsedSeconds: -0.01, createdUtc: utc),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new ConversationStageTiming("parse", -0.01),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new ConversationError("code", "stage", "summary", new string('x', 2049), null),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public async Task Save_retains_only_the_newest_fifty_messages()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var original = await store.LoadOrCreateAsync("program-retention");
            var messages = Enumerable.Range(0, 55)
                .Select(index => CreateMessage(id: "message-" + index, text: "text-" + index))
                .ToArray();

            await store.SaveAsync(new ConversationHistory(1, original.ConversationId, messages));
            var history = await store.LoadOrCreateAsync("program-retention");

            Assert.That(history.Messages, Has.Count.EqualTo(50));
            Assert.That(history.Messages[0].Id, Is.EqualTo("message-5"));
            Assert.That(history.Messages[49].Id, Is.EqualTo("message-54"));
        }

        [Test]
        public async Task Linking_before_and_after_program_hashes_preserves_the_conversation_id()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var beforeApply = await store.LoadOrCreateAsync("program-before");

            await store.LinkProgramHashAsync(beforeApply.ConversationId, "program-after");
            var afterApply = await new FileConversationStore(temporary.Path)
                .LoadOrCreateAsync("program-after");

            Assert.That(afterApply.ConversationId, Is.EqualTo(beforeApply.ConversationId));
        }

        [Test]
        public async Task Separate_instances_concurrently_create_one_alias_for_the_same_program_hash()
        {
            using var temporary = new TemporaryDirectory();
            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loads = Enumerable.Range(0, 32)
                .Select(
                    async _ =>
                    {
                        var store = new FileConversationStore(temporary.Path);
                        await start.Task;
                        return await store.LoadOrCreateAsync("program-shared");
                    })
                .ToArray();

            start.SetResult(true);
            var histories = await Task.WhenAll(loads);

            Assert.That(
                histories.Select(history => history.ConversationId).Distinct().Count(),
                Is.EqualTo(1));
        }

        [Test]
        public async Task Separate_instances_concurrently_preserve_every_program_alias()
        {
            using var temporary = new TemporaryDirectory();
            var start = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var loads = Enumerable.Range(0, 32)
                .Select(
                    async index =>
                    {
                        var store = new FileConversationStore(temporary.Path);
                        await start.Task;
                        return (
                            ProgramHash: "program-" + index,
                            History: await store.LoadOrCreateAsync("program-" + index));
                    })
                .ToArray();

            start.SetResult(true);
            var created = await Task.WhenAll(loads);

            foreach (var item in created)
            {
                var reloaded = await new FileConversationStore(temporary.Path)
                    .LoadOrCreateAsync(item.ProgramHash);
                Assert.That(reloaded.ConversationId, Is.EqualTo(item.History.ConversationId));
            }
        }

        [Test]
        public void General_conversation_cannot_be_linked_to_a_program_hash()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);

            var exception = Assert.ThrowsAsync<ArgumentException>(
                async () => await store.LinkProgramHashAsync("general", "program-hash"));

            Assert.That(exception!.ParamName, Is.EqualTo("conversationId"));
            Assert.That(
                File.Exists(Path.Combine(temporary.Path, "Conversations", "index.json")),
                Is.False);
        }

        [Test]
        public async Task Complete_message_record_round_trips_every_persisted_field()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var history = await store.LoadOrCreateAsync("program-round-trip");
            var createdUtc = new DateTime(2026, 7, 29, 12, 34, 56, DateTimeKind.Utc);
            var message = CreateMessage(
                id: "complete-record",
                role: ConversationRole.System,
                kind: ConversationMessageKind.Error,
                mode: ConversationMode.Modify,
                text: "\u4fee\u6539\u5931\u8d25",
                reasoningSummary: "Provider summary",
                stages: new[] { new ConversationStageTiming("validation", 2.25) },
                elapsedSeconds: 3.5,
                error: new ConversationError(
                    "invalid_patch",
                    "validation",
                    "Safe summary",
                    "Safe technical details",
                    "/Program/Instructions"),
                createdUtc: createdUtc);

            await store.SaveAsync(
                new ConversationHistory(1, history.ConversationId, new[] { message }));
            var loaded = (await store.LoadOrCreateAsync("program-round-trip")).Messages.Single();

            Assert.That(loaded.Id, Is.EqualTo("complete-record"));
            Assert.That(loaded.Role, Is.EqualTo(ConversationRole.System));
            Assert.That(loaded.Kind, Is.EqualTo(ConversationMessageKind.Error));
            Assert.That(loaded.Mode, Is.EqualTo(ConversationMode.Modify));
            Assert.That(loaded.Text, Is.EqualTo("\u4fee\u6539\u5931\u8d25"));
            Assert.That(loaded.ReasoningSummary, Is.EqualTo("Provider summary"));
            Assert.That(loaded.Stages.Single().Stage, Is.EqualTo("validation"));
            Assert.That(loaded.Stages.Single().ElapsedSeconds, Is.EqualTo(2.25));
            Assert.That(loaded.ElapsedSeconds, Is.EqualTo(3.5));
            Assert.That(loaded.Error, Is.Not.Null);
            Assert.That(loaded.Error!.Code, Is.EqualTo("invalid_patch"));
            Assert.That(loaded.Error.Stage, Is.EqualTo("validation"));
            Assert.That(loaded.Error.Summary, Is.EqualTo("Safe summary"));
            Assert.That(loaded.Error.TechnicalDetails, Is.EqualTo("Safe technical details"));
            Assert.That(loaded.Error.Path, Is.EqualTo("/Program/Instructions"));
            Assert.That(loaded.CreatedUtc, Is.EqualTo(createdUtc));
            Assert.That(loaded.CreatedUtc.Kind, Is.EqualTo(DateTimeKind.Utc));
        }

        [Test]
        public async Task General_history_is_stable_and_separate_from_program_histories()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);

            var firstGeneral = await store.LoadOrCreateAsync(null);
            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    firstGeneral.ConversationId,
                    new[] { CreateMessage(text: "general message") }));
            var secondGeneral = await store.LoadOrCreateAsync(null);
            var program = await store.LoadOrCreateAsync("program-a");

            Assert.That(secondGeneral.ConversationId, Is.EqualTo(firstGeneral.ConversationId));
            Assert.That(secondGeneral.Messages.Single().Text, Is.EqualTo("general message"));
            Assert.That(program.ConversationId, Is.Not.EqualTo(firstGeneral.ConversationId));
            Assert.That(File.Exists(Path.Combine(temporary.Path, "Conversations", "general.json")), Is.True);
        }

        [Test]
        public async Task Clear_removes_messages_without_changing_the_resolved_alias()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var original = await store.LoadOrCreateAsync("program-clear");
            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    original.ConversationId,
                    new[] { CreateMessage(text: "remove me") }));

            await store.ClearAsync(original.ConversationId);
            var cleared = await store.LoadOrCreateAsync("program-clear");

            Assert.That(cleared.ConversationId, Is.EqualTo(original.ConversationId));
            Assert.That(cleared.Messages, Is.Empty);
        }

        [Test]
        public async Task Sensitive_values_in_message_fields_are_redacted_without_altering_ordinary_chat()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var history = await store.LoadOrCreateAsync("program-secret");
            const string programXml = "<Program name=\"secret\"><Instructions /></Program>";
            const string rawPatch = "{\"operations\":[{\"op\":\"remove\"}]}";
            const string authorization = "Authorization: Bearer secret-token";
            const string apiKey = "sk-sensitive123456";
            const string ordinary =
                "Program diagrams, operations planning, and bearer authentication concepts remain useful.";

            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    history.ConversationId,
                    new[]
                    {
                        CreateMessage(
                            id: "sensitive",
                            kind: ConversationMessageKind.Error,
                            text: programXml,
                            reasoningSummary: rawPatch,
                            error: new ConversationError(
                                "request_failed",
                                "transport",
                                "Request failed",
                                authorization + " " + apiKey,
                                null)),
                        CreateMessage(id: "ordinary", text: ordinary)
                    }));

            var historyPath = Path.Combine(
                temporary.Path,
                "Conversations",
                history.ConversationId + ".json");
            var persisted = File.ReadAllText(historyPath);
            var loaded = await store.LoadOrCreateAsync("program-secret");
            Assert.That(persisted, Does.Not.Contain("<Program"));
            Assert.That(persisted, Does.Not.Contain("\"operations\""));
            Assert.That(persisted, Does.Not.Contain("Bearer"));
            Assert.That(persisted, Does.Not.Contain(apiKey));
            Assert.That(loaded.Messages.Single(message => message.Id == "ordinary").Text, Is.EqualTo(ordinary));
            Assert.That(loaded.Messages.Single(message => message.Id == "sensitive").Text, Does.Contain("redacted"));
            Assert.That(File.ReadAllBytes(historyPath).Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.That(
                Directory.GetFiles(temporary.Path, "*.tmp", SearchOption.AllDirectories),
                Is.Empty);
        }

        [Test]
        public async Task Malformed_index_fails_open_without_modifying_the_bad_file()
        {
            using var temporary = new TemporaryDirectory();
            var warnings = new List<string>();
            var conversations = Path.Combine(temporary.Path, "Conversations");
            Directory.CreateDirectory(conversations);
            var indexPath = Path.Combine(conversations, "index.json");
            const string malformed = "{\"aliases\":{\"Bearer secret-token\":";
            File.WriteAllText(indexPath, malformed);
            var store = new FileConversationStore(temporary.Path, warnings.Add);

            var history = await store.LoadOrCreateAsync("program-corrupt-index");

            Assert.That(history.ConversationId, Is.Not.Empty);
            Assert.That(history.Messages, Is.Empty);
            Assert.That(File.ReadAllText(indexPath), Is.EqualTo(malformed));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Not.Contain("Bearer"));
            Assert.That(warnings[0].Length, Is.LessThanOrEqualTo(2048));
        }

        [Test]
        public async Task Malformed_history_fails_open_with_the_same_id_and_preserves_the_bad_file()
        {
            using var temporary = new TemporaryDirectory();
            var warnings = new List<string>();
            var store = new FileConversationStore(temporary.Path, warnings.Add);
            var original = await store.LoadOrCreateAsync("program-corrupt-history");
            var historyPath = Path.Combine(
                temporary.Path,
                "Conversations",
                original.ConversationId + ".json");
            Directory.CreateDirectory(Path.GetDirectoryName(historyPath)!);
            const string malformed = "{\"messages\":[\"Bearer secret-token\"";
            File.WriteAllText(historyPath, malformed);

            var recovered = await store.LoadOrCreateAsync("program-corrupt-history");

            Assert.That(recovered.ConversationId, Is.EqualTo(original.ConversationId));
            Assert.That(recovered.Messages, Is.Empty);
            Assert.That(File.ReadAllText(historyPath), Is.EqualTo(malformed));
            Assert.That(warnings, Has.Count.EqualTo(1));
            Assert.That(warnings[0], Does.Not.Contain("Bearer"));
        }

        [Test]
        public async Task Throwing_warning_sink_does_not_break_corruption_recovery()
        {
            using var temporary = new TemporaryDirectory();
            var conversations = Path.Combine(temporary.Path, "Conversations");
            Directory.CreateDirectory(conversations);
            File.WriteAllText(Path.Combine(conversations, "index.json"), "{malformed");
            var store = new FileConversationStore(
                temporary.Path,
                _ => throw new InvalidOperationException("sink failed"));

            var recovered = await store.LoadOrCreateAsync("program-warning-sink");

            Assert.That(recovered.ConversationId, Is.Not.Empty);
            Assert.That(recovered.Messages, Is.Empty);
        }

        [Test]
        public async Task Cancelled_replacement_preserves_the_previous_history()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var history = await store.LoadOrCreateAsync("program-cancelled-save");
            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    history.ConversationId,
                    new[] { CreateMessage(text: "previous") }));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await store.SaveAsync(
                    new ConversationHistory(
                        1,
                        history.ConversationId,
                        new[] { CreateMessage(text: "replacement") }),
                    cancellation.Token));

            var loaded = await store.LoadOrCreateAsync("program-cancelled-save");
            Assert.That(loaded.Messages.Single().Text, Is.EqualTo("previous"));
        }

        [Test]
        public async Task Failed_replacement_preserves_previous_history_and_removes_temporary_file()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var history = await store.LoadOrCreateAsync("program-failed-save");
            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    history.ConversationId,
                    new[] { CreateMessage(text: "previous") }));
            var historyPath = Path.Combine(
                temporary.Path,
                "Conversations",
                history.ConversationId + ".json");

            using (File.Open(historyPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.ThrowsAsync<IOException>(
                    async () => await store.SaveAsync(
                        new ConversationHistory(
                            1,
                            history.ConversationId,
                            new[] { CreateMessage(text: "replacement") })));
            }

            var loaded = await store.LoadOrCreateAsync("program-failed-save");
            Assert.That(loaded.Messages.Single().Text, Is.EqualTo("previous"));
            Assert.That(
                Directory.GetFiles(temporary.Path, "*.tmp", SearchOption.AllDirectories),
                Is.Empty);
        }

        private static ConversationMessage CreateMessage(
            string id = "message-id",
            ConversationRole role = ConversationRole.Assistant,
            ConversationMessageKind kind = ConversationMessageKind.Message,
            ConversationMode mode = ConversationMode.Ask,
            string text = "visible text",
            string? reasoningSummary = null,
            IReadOnlyList<ConversationStageTiming>? stages = null,
            double? elapsedSeconds = 1.5,
            ConversationError? error = null,
            DateTime? createdUtc = null)
        {
            return new ConversationMessage(
                id,
                role,
                kind,
                mode,
                text,
                reasoningSummary,
                stages ?? new[] { new ConversationStageTiming("complete", 1.5) },
                elapsedSeconds,
                error,
                createdUtc ?? new DateTime(2026, 7, 29, 12, 0, 0, DateTimeKind.Utc));
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    TestContext.CurrentContext.WorkDirectory,
                    "conversation-store-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
