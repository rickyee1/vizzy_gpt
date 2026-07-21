#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Storage;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class SettingsDialogControllerTests
    {
        [Test]
        public void Save_persists_only_non_secret_fields_and_dpapi_ciphertext()
        {
            var store = new FakeStore();
            var controller = new SettingsDialogController(
                store,
                value => "protected:" + value,
                value => value.Substring("protected:".Length),
                (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())));
            var draft = new VizzyGptSettingsDraft(
                "https://api.example.test/v1",
                ApiMode.Auto,
                "gpt-test",
                "plain-api-key",
                30);

            controller.SaveAsync(draft).GetAwaiter().GetResult();

            Assert.That(store.Settings, Is.Not.Null);
            Assert.That(store.Settings!.BaseUrl, Is.EqualTo(draft.BaseUrl));
            Assert.That(store.ProtectedApiKey, Is.EqualTo("protected:plain-api-key"));
            Assert.That(store.Settings.BaseUrl, Does.Not.Contain("plain-api-key"));
            Assert.That(controller.DestinationHost, Is.EqualTo("api.example.test"));
        }

        [Test]
        public void Test_connection_uses_no_program_context_and_does_not_persist_draft()
        {
            var store = new FakeStore();
            AiRequest? observed = null;
            var controller = new SettingsDialogController(
                store,
                value => "protected:" + value,
                value => value.Substring("protected:".Length),
                (request, _) =>
                {
                    observed = request;
                    return Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>()));
                });
            var draft = new VizzyGptSettingsDraft(
                "http://127.0.0.1:8787",
                ApiMode.ChatCompletions,
                "local-model",
                "plain-api-key",
                15);

            var result = controller.TestConnectionAsync(draft).GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(observed, Is.Not.Null);
            Assert.That(observed!.Context, Does.Not.Contain("<Program"));
            Assert.That(store.SaveSettingsCalls, Is.EqualTo(0));
            Assert.That(store.SaveProtectedApiKeyCalls, Is.EqualTo(0));
        }

        [Test]
        public void Cancel_test_connection_cancels_the_owned_request()
        {
            var completion = new TaskCompletionSource<AiResponse>();
            var observed = default(CancellationToken);
            var controller = new SettingsDialogController(
                new FakeStore(),
                value => value,
                value => value,
                (_, cancellationToken) =>
                {
                    observed = cancellationToken;
                    return completion.Task;
                });
            var draft = new VizzyGptSettingsDraft("https://api.example.test", ApiMode.Auto, "test", "key", 10);

            var pending = controller.TestConnectionAsync(draft);
            controller.CancelTestConnection();
            Assert.That(observed.IsCancellationRequested, Is.True);
            completion.TrySetCanceled(observed);
            var result = pending.GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("Connection test cancelled."));
        }

        private sealed class FakeStore : IDataStore
        {
            public NonSecretSettings? Settings { get; private set; }

            public string? ProtectedApiKey { get; private set; }

            public int SaveSettingsCalls { get; private set; }

            public int SaveProtectedApiKeyCalls { get; private set; }

            public Task SaveNonSecretSettingsAsync(NonSecretSettings settings, CancellationToken cancellationToken = default)
            {
                Settings = settings;
                SaveSettingsCalls++;
                return Task.CompletedTask;
            }

            public Task<NonSecretSettings?> LoadNonSecretSettingsAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(Settings);
            }

            public Task SaveProtectedApiKeyAsync(string protectedApiKey, CancellationToken cancellationToken = default)
            {
                ProtectedApiKey = protectedApiKey;
                SaveProtectedApiKeyCalls++;
                return Task.CompletedTask;
            }

            public Task<string?> LoadProtectedApiKeyAsync(CancellationToken cancellationToken = default)
            {
                return Task.FromResult(ProtectedApiKey);
            }

            public Task SaveBackupAsync(string programFingerprint, string xml, DateTime createdUtc, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task SavePendingAsync(PendingChange pending, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }

            public Task<PendingChange?> LoadPendingAsync(string programFingerprint, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<PendingChange?>(null);
            }

            public Task DeletePendingAsync(string programFingerprint, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }
    }
}
