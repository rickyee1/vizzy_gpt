using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Api;
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
        public async Task Non_secret_settings_round_trip_atomically_without_serializing_an_api_key()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var first = new NonSecretSettings(
                "https://api.example.test",
                ApiMode.Auto,
                "gpt-test",
                30);
            var replacement = new NonSecretSettings(
                "http://127.0.0.1:8787",
                ApiMode.ChatCompletions,
                "local-model",
                45);

            await store.SaveNonSecretSettingsAsync(first);
            await store.SaveNonSecretSettingsAsync(replacement);

            var loaded = await store.LoadNonSecretSettingsAsync();
            var settingsFiles = Directory.GetFiles(temporary.Path, "*.json", SearchOption.AllDirectories);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.BaseUrl, Is.EqualTo(replacement.BaseUrl));
            Assert.That(loaded.Mode, Is.EqualTo(replacement.Mode));
            Assert.That(loaded.Model, Is.EqualTo(replacement.Model));
            Assert.That(loaded.TimeoutSeconds, Is.EqualTo(replacement.TimeoutSeconds));
            Assert.That(settingsFiles, Has.Length.EqualTo(1));
            Assert.That(File.ReadAllText(settingsFiles[0]), Does.Not.Contain("apiKey").IgnoreCase);
            Assert.That(Directory.GetFiles(temporary.Path, "*.tmp", SearchOption.AllDirectories), Is.Empty);
        }

        [Test]
        public async Task Protected_api_key_ciphertext_is_stored_separately_from_non_secret_settings()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var settings = new NonSecretSettings(
                "https://api.example.test",
                ApiMode.Responses,
                "gpt-test",
                30);
            const string protectedApiKey = "DPAPI-CIPHERTEXT-ONLY";

            await store.SaveNonSecretSettingsAsync(settings);
            await store.SaveProtectedApiKeyAsync(protectedApiKey);

            var json = File.ReadAllText(Directory.GetFiles(temporary.Path, "*.json", SearchOption.AllDirectories).Single());
            var loadedCiphertext = await store.LoadProtectedApiKeyAsync();

            Assert.That(loadedCiphertext, Is.EqualTo(protectedApiKey));
            Assert.That(json, Does.Not.Contain(protectedApiKey));
            Assert.That(Directory.GetFiles(temporary.Path, "*.tmp", SearchOption.AllDirectories), Is.Empty);
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

        [Test]
        public async Task Load_rejects_self_consistent_result_xml_and_hash_that_are_not_the_patch_output()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-result-integrity", "patched");
            await store.SavePendingAsync(pending);
            MutatePendingJson(
                temporary.Path,
                json =>
                {
                    json["resultXml"] = pending.BaseXml;
                    json["resultHash"] = pending.BaseHash;
                });

            await AssertLoadRejectedAsync(store, pending.ProgramFingerprint);
        }

        [TestCase("targetFingerprints", "remove")]
        [TestCase("targetFingerprints", "empty")]
        [TestCase("targetFingerprints", "alter")]
        [TestCase("declarationFingerprints", "remove")]
        [TestCase("declarationFingerprints", "empty")]
        [TestCase("declarationFingerprints", "alter")]
        public async Task Load_rejects_fingerprint_sets_that_do_not_match_recomputation(
            string propertyName,
            string mutation)
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePendingWithDeclaration("program-fingerprint-integrity");
            await store.SavePendingAsync(pending);
            MutatePendingJson(
                temporary.Path,
                json =>
                {
                    if (string.Equals(mutation, "remove", StringComparison.Ordinal))
                    {
                        json.Remove(propertyName);
                    }
                    else if (string.Equals(mutation, "empty", StringComparison.Ordinal))
                    {
                        json[propertyName] = new JArray();
                    }
                    else
                    {
                        var fingerprints = (JArray)json[propertyName]!;
                        ((JObject)fingerprints[0]!)["hash"] = new string('0', 64);
                    }
                });

            await AssertLoadRejectedAsync(store, pending.ProgramFingerprint);
        }

        [Test]
        public async Task Load_rejects_a_file_whose_programFingerprint_differs_from_the_requested_fingerprint()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-requested", "patched");
            await store.SavePendingAsync(pending);
            MutatePendingJson(
                temporary.Path,
                json => json["programFingerprint"] = "program-other");

            await AssertLoadRejectedAsync(store, pending.ProgramFingerprint);
        }

        [Test]
        public async Task Load_rejects_persisted_empty_preview_lines()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-empty-preview", "patched");
            await store.SavePendingAsync(pending);
            MutatePendingJson(
                temporary.Path,
                json => json["previewLines"] = new JArray());

            await AssertLoadRejectedAsync(store, pending.ProgramFingerprint);
        }

        [Test]
        public async Task Load_rejects_an_altered_deterministic_patch_change_preview_line()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var pending = CreatePending("program-altered-preview", "patched");
            Assert.That(pending.PreviewLines, Is.Not.Empty);
            await store.SavePendingAsync(pending);
            MutatePendingJson(
                temporary.Path,
                json => ((JArray)json["previewLines"]!)[0] = "Altered deterministic change line");

            await AssertLoadRejectedAsync(store, pending.ProgramFingerprint);
        }

        [Test]
        public async Task Load_accepts_exact_patch_change_prefix_followed_by_ChangeSession_warning_lines()
        {
            using var temporary = new TemporaryDirectory();
            IDataStore store = new FileDataStore(temporary.Path);
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>");
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Apply warning preview",
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.UpdateAttribute,
                        target: new NodeSelector(1, null),
                        attribute: "text",
                        value: "after")
                });
            var applied = VizzyPatchEngine.Apply(document, patch);
            var warning = new ValidationIssue(
                ValidationSeverity.Warning,
                "RuntimeWarning",
                "Serializer retained a warning.");
            var session = ChangeSession.Create(
                document,
                patch,
                applied,
                new ValidationReport(new[] { warning }));
            var pending = PendingChange.Create(
                "program-warning-preview",
                session,
                new DateTime(2026, 7, 21, 2, 0, 0, DateTimeKind.Utc));

            await store.SavePendingAsync(pending);
            var loaded = await store.LoadPendingAsync(pending.ProgramFingerprint);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(
                loaded!.PreviewLines.Take(applied.Changes.Count),
                Is.EqualTo(applied.Changes));
            Assert.That(
                loaded.PreviewLines.Skip(applied.Changes.Count),
                Is.EqualTo(new[] { warning.Message }));
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

        private static PendingChange CreatePendingWithDeclaration(string fingerprint)
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables><Variable name='pitch' number='0' /></Variables>" +
                "<Instructions><Log id='1' text='before' variableName='pitch' /></Instructions>" +
                "<Expressions /></Program>");
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Update referenced target",
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.UpdateAttribute,
                        target: new NodeSelector(1, null),
                        attribute: "text",
                        value: "after")
                });
            var patchResult = VizzyPatchEngine.Apply(document, patch);
            var session = ChangeSession.Create(
                document,
                patch,
                patchResult,
                new ValidationReport(Array.Empty<ValidationIssue>()));
            return PendingChange.Create(
                fingerprint,
                session,
                new DateTime(2026, 7, 21, 1, 0, 0, DateTimeKind.Utc));
        }

        private static async Task AssertLoadRejectedAsync(IDataStore store, string programFingerprint)
        {
            Exception? failure = null;
            try
            {
                _ = await store.LoadPendingAsync(programFingerprint);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            Assert.That(failure, Is.Not.Null);
        }

        private static void MutatePendingJson(string root, Action<JObject> mutation)
        {
            var path = Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).Single();
            var json = JObject.Parse(File.ReadAllText(path));
            mutation(json);
            File.WriteAllText(
                path,
                json.ToString(Formatting.None),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
