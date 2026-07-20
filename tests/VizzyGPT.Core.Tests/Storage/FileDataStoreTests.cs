using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Storage;
using VizzyGPT.Core.Validation;

namespace VizzyGPT.Core.Tests.Storage
{
    public sealed class FileDataStoreTests
    {
        [Test]
        public async Task Pending_change_save_load_delete_round_trips_all_persisted_fields()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-alpha", "first");

            await store.SavePendingAsync(pending);
            var loaded = await store.LoadPendingAsync(pending.ProgramFingerprint);

            AssertPendingEquivalent(loaded, pending);

            await store.DeletePendingAsync(pending.ProgramFingerprint);

            Assert.That(await store.LoadPendingAsync(pending.ProgramFingerprint), Is.Null);
            Assert.That(Directory.GetFiles(temporary.Path, "*.json", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public async Task Saves_text_as_utf8_without_a_byte_order_mark()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-utf8", "\u706b\u7bad");

            await store.SavePendingAsync(pending);
            await store.SaveBackupAsync(
                pending.ProgramFingerprint,
                "<Program name='\u706b\u7bad' />",
                new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));

            var persistedFiles = Directory.GetFiles(temporary.Path, "*", SearchOption.AllDirectories);
            Assert.That(persistedFiles, Has.Length.EqualTo(2));
            foreach (var path in persistedFiles)
            {
                var bytes = File.ReadAllBytes(path);
                Assert.That(bytes, Is.Not.Empty);
                Assert.That(HasUtf8Bom(bytes), Is.False, path);
            }
        }

        [Test]
        public async Task Fingerprints_are_sanitized_and_pending_paths_stay_under_the_caller_root()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            const string fingerprint = "..\\craft:name/one?*";

            await store.SavePendingAsync(CreatePending(fingerprint, "sanitized"));

            var pendingFile = Directory.GetFiles(temporary.Path, "*.json", SearchOption.AllDirectories).Single();
            var expectedName = Sanitize(fingerprint) + ".json";
            var rootPrefix = Path.GetFullPath(temporary.Path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            Assert.That(Path.GetFileName(pendingFile), Is.EqualTo(expectedName));
            Assert.That(Path.GetFullPath(pendingFile).StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase), Is.True);
        }

        [Test]
        public async Task Pending_save_moves_then_replaces_through_a_temp_sibling_without_leaving_artifacts()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var first = CreatePending("program-atomic", "first");
            var second = CreatePending("program-atomic", "second");

            await store.SavePendingAsync(first);
            Assert.That(Directory.GetFiles(temporary.Path, "*", SearchOption.AllDirectories), Has.Length.EqualTo(1));

            await store.SavePendingAsync(second);

            var files = Directory.GetFiles(temporary.Path, "*", SearchOption.AllDirectories);
            Assert.That(files, Has.Length.EqualTo(1));
            Assert.That(Path.GetExtension(files[0]), Is.EqualTo(".json"));
            AssertPendingEquivalent(await store.LoadPendingAsync("program-atomic"), second);
        }

        [Test]
        public async Task Twenty_first_backup_removes_only_the_oldest_and_retains_the_newest_twenty()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var firstCreatedUtc = new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

            var writeOrder = Enumerable.Range(1, 20).Concat(new[] { 0 });
            foreach (var index in writeOrder)
            {
                await store.SaveBackupAsync(
                    "program-retention",
                    "<Program index='" + index + "' />",
                    firstCreatedUtc.AddMinutes(index));
            }

            var backupFiles = Directory.GetFiles(temporary.Path, "*.xml", SearchOption.AllDirectories);
            var retainedIndexes = backupFiles
                .Select(path => int.Parse(XElement.Parse(File.ReadAllText(path)).Attribute("index")!.Value))
                .OrderBy(index => index)
                .ToArray();

            Assert.That(backupFiles, Has.Length.EqualTo(20));
            Assert.That(retainedIndexes, Is.EqualTo(Enumerable.Range(1, 20).ToArray()));
        }

        [Test]
        public void Backup_write_failure_is_surfaced_before_caller_mutation_can_run()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var backupsPath = Path.Combine(temporary.Path, "Backups");
            if (Directory.Exists(backupsPath))
            {
                Directory.Delete(backupsPath);
            }

            File.WriteAllText(backupsPath, "blocks backup directory creation");
            var callerMutated = false;

            var exception = Assert.ThrowsAsync<IOException>(
                async () =>
                {
                    await store.SaveBackupAsync(
                        "program-failure",
                        "<Program />",
                        new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));
                    callerMutated = true;
                });

            Assert.That(exception!.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(callerMutated, Is.False);
        }

        private static PendingChange CreatePending(string fingerprint, string revision)
        {
            var document = VizzyProgramDocument.Parse(
                "<Program name='" + revision + "'><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>");
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Apply " + revision,
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.UpdateAttribute,
                        target: new NodeSelector(1, null),
                        attribute: "text",
                        value: revision)
                });
            var patchResult = VizzyPatchEngine.Apply(document, patch);
            var session = ChangeSession.Create(
                document,
                patch,
                patchResult,
                new ValidationReport(Array.Empty<ValidationIssue>()));
            var createdUtc = string.Equals(revision, "second", StringComparison.Ordinal)
                ? new DateTime(2026, 7, 21, 0, 0, 1, DateTimeKind.Utc)
                : new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc);

            return PendingChange.Create(fingerprint, session, createdUtc);
        }

        private static void AssertPendingEquivalent(PendingChange? actual, PendingChange expected)
        {
            Assert.That(actual, Is.Not.Null);
            Assert.That(actual!.ProgramFingerprint, Is.EqualTo(expected.ProgramFingerprint));
            Assert.That(actual.BaseXml, Is.EqualTo(expected.BaseXml));
            Assert.That(actual.BaseHash, Is.EqualTo(expected.BaseHash));
            Assert.That(JToken.DeepEquals(JObject.Parse(actual.PatchJson), JObject.Parse(expected.PatchJson)), Is.True);
            Assert.That(actual.ResultXml, Is.EqualTo(expected.ResultXml));
            Assert.That(actual.ResultHash, Is.EqualTo(expected.ResultHash));
            Assert.That(actual.CreatedUtc, Is.EqualTo(expected.CreatedUtc));
            Assert.That(actual.PreviewLines, Is.EqualTo(expected.PreviewLines));
            Assert.That(
                actual.TargetFingerprints.Select(item => new { item.Selector.Id, item.Selector.Path, item.Hash }),
                Is.EqualTo(expected.TargetFingerprints.Select(item => new { item.Selector.Id, item.Selector.Path, item.Hash })));
            Assert.That(
                actual.DeclarationFingerprints.Select(item => new { item.Kind, item.Name, item.Hash }),
                Is.EqualTo(expected.DeclarationFingerprints.Select(item => new { item.Kind, item.Name, item.Hash })));
        }

        private static bool HasUtf8Bom(IReadOnlyList<byte> bytes) =>
            bytes.Count >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;

        private static string Sanitize(string fingerprint)
        {
            var invalid = new HashSet<char>(Path.GetInvalidFileNameChars());
            return new string(fingerprint.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        }

        private sealed class TemporaryDirectory : IDisposable
        {
            public TemporaryDirectory()
            {
                Path = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    "VizzyGPT.Core.Tests",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Path);
            }

            public string Path { get; }

            public void Dispose()
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, recursive: true);
                }
            }
        }
    }
}
