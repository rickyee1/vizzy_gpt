using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public async Task Conversation_files_are_atomic_utf8_without_bom_and_exclude_sensitive_request_data()
        {
            using var temporary = new TemporaryDirectory();
            var store = new FileConversationStore(temporary.Path);
            var history = await store.LoadOrCreateAsync("program-secret");
            var programXml = "<Program name=\"secret\" />";
            var rawPatch = "{\"operations\":[{\"op\":\"remove\"}]}";
            var authorization = "Bearer secret-token";

            await store.SaveAsync(
                new ConversationHistory(
                    1,
                    history.ConversationId,
                    new[] { CreateMessage(text: "Safe visible response \u706b\u7bad") }));

            var historyPath = Path.Combine(
                temporary.Path,
                "Conversations",
                history.ConversationId + ".json");
            var persisted = File.ReadAllText(historyPath);
            Assert.That(persisted, Does.Not.Contain(programXml.Substring(0, 8)));
            Assert.That(persisted, Does.Not.Contain("\"operations\""));
            Assert.That(persisted, Does.Not.Contain(authorization.Substring(0, 6)));
            Assert.That(File.ReadAllBytes(historyPath).Take(3), Is.Not.EqualTo(new byte[] { 0xef, 0xbb, 0xbf }));
            Assert.That(
                Directory.GetFiles(temporary.Path, "*.tmp", SearchOption.AllDirectories),
                Is.Empty);

            GC.KeepAlive(rawPatch);
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
