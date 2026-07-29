#nullable enable

using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
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
                (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                _ => Task.CompletedTask);
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
                },
                _ => Task.CompletedTask);
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
                },
                _ => Task.CompletedTask);
            var draft = new VizzyGptSettingsDraft("https://api.example.test", ApiMode.Auto, "test", "key", 10);

            var pending = controller.TestConnectionAsync(draft);
            controller.CancelTestConnection();
            Assert.That(observed.IsCancellationRequested, Is.True);
            completion.TrySetCanceled(observed);
            var result = pending.GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo("Connection test cancelled."));
        }

        [Test]
        public void Clear_history_calls_callback_once_without_persisting_settings()
        {
            var store = new FakeStore();
            var clearCalls = 0;
            var controller = new SettingsDialogController(
                store,
                value => value,
                value => value,
                (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                _ =>
                {
                    clearCalls++;
                    return Task.CompletedTask;
                });

            var result = controller.ClearHistoryAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Status, Is.EqualTo("Current conversation cleared."));
            Assert.That(clearCalls, Is.EqualTo(1));
            Assert.That(store.SaveSettingsCalls, Is.Zero);
            Assert.That(store.SaveProtectedApiKeyCalls, Is.Zero);
        }

        [Test]
        public void Clear_history_failure_is_nonfatal_and_sanitized()
        {
            var controller = new SettingsDialogController(
                new FakeStore(),
                value => value,
                value => value,
                (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                _ => throw new InvalidOperationException("Authorization: Bearer clear-secret"));

            var result = controller.ClearHistoryAsync().GetAwaiter().GetResult();

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Does.Contain("[REDACTED]"));
            Assert.That(result.Status, Does.Not.Contain("clear-secret"));
        }

        [Test]
        public void Clear_history_button_does_not_close_dialog()
        {
            var root = new GameObject("settings-clear-history-test");
            try
            {
                var view = root.AddComponent<SettingsDialogViewController>();
                var controller = new SettingsDialogController(
                    new FakeStore(),
                    value => value,
                    value => value,
                    (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                    _ => Task.CompletedTask);
                var closeCalls = 0;
                view.Configure(controller, () => { }, () => closeCalls++);

                view.OnClearHistoryButtonClicked();

                Assert.That(closeCalls, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator Rapid_clear_clicks_share_one_owned_operation_and_toggle_button()
        {
            var root = new GameObject("settings-clear-history-double-click-test");
            var buttonRoot = new GameObject("clear-button", typeof(RectTransform), typeof(Button));
            var statusRoot = new GameObject("clear-status", typeof(RectTransform), typeof(TestTmpText));
            buttonRoot.transform.SetParent(root.transform, false);
            statusRoot.transform.SetParent(root.transform, false);
            try
            {
                var completion = new TaskCompletionSource<bool>();
                var clearCalls = 0;
                var store = new FakeStore();
                var controller = new SettingsDialogController(
                    store,
                    value => value,
                    value => value,
                    (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                    async cancellationToken =>
                    {
                        clearCalls++;
                        await completion.Task;
                        cancellationToken.ThrowIfCancellationRequested();
                    });
                var view = root.AddComponent<SettingsDialogViewController>();
                var closeCalls = 0;
                view.Configure(controller, () => { }, () => closeCalls++);
                var button = buttonRoot.GetComponent<Button>();
                var status = statusRoot.GetComponent<TestTmpText>();
                SetPrivateField(view, "clearHistoryButton", button);
                SetPrivateField(view, "connectionStatusText", status);

                view.OnClearHistoryButtonClicked();
                view.OnClearHistoryButtonClicked();

                Assert.That(clearCalls, Is.EqualTo(1));
                Assert.That(button.interactable, Is.False);
                completion.SetResult(true);
                for (var attempt = 0; attempt < 100 && !button.interactable; attempt++)
                {
                    yield return null;
                }

                Assert.That(button.interactable, Is.True);
                Assert.That(status.text, Is.EqualTo("Current conversation cleared."));
                Assert.That(closeCalls, Is.Zero);
                Assert.That(store.SaveSettingsCalls, Is.Zero);
                Assert.That(store.SaveProtectedApiKeyCalls, Is.Zero);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator Destroy_cancels_clear_and_suppresses_disposed_status_update()
        {
            var root = new GameObject("settings-clear-history-dispose-test");
            var buttonRoot = new GameObject("clear-button", typeof(RectTransform), typeof(Button));
            var statusRoot = new GameObject("clear-status", typeof(RectTransform), typeof(TestTmpText));
            buttonRoot.transform.SetParent(root.transform, false);
            try
            {
                var observedToken = default(CancellationToken);
                var cancellationObserved = false;
                var started = new TaskCompletionSource<bool>();
                var completion = new TaskCompletionSource<bool>();
                var controller = new SettingsDialogController(
                    new FakeStore(),
                    value => value,
                    value => value,
                    (_, __) => Task.FromResult(new AiResponse("ok", null, false, Array.Empty<string>())),
                    async cancellationToken =>
                    {
                        observedToken = cancellationToken;
                        started.TrySetResult(true);
                        using (cancellationToken.Register(() =>
                        {
                            cancellationObserved = true;
                            completion.TrySetCanceled();
                        }))
                        {
                            await completion.Task;
                        }
                    });
                var view = root.AddComponent<SettingsDialogViewController>();
                view.Configure(controller, () => { }, () => { });
                var status = statusRoot.GetComponent<TestTmpText>();
                status.text = "unchanged";
                SetPrivateField(view, "clearHistoryButton", buttonRoot.GetComponent<Button>());
                SetPrivateField(view, "connectionStatusText", status);

                view.OnClearHistoryButtonClicked();
                Assert.That(started.Task.IsCompleted, Is.True);
                Assert.That(buttonRoot.GetComponent<Button>().interactable, Is.False);
                InvokePrivateMethod(view, "OnDestroy");
                UnityEngine.Object.DestroyImmediate(root);
                yield return null;

                Assert.That(cancellationObserved, Is.True);
                Assert.That(observedToken.IsCancellationRequested, Is.True);
                Assert.That(status.text, Is.EqualTo("unchanged"));
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                UnityEngine.Object.DestroyImmediate(statusRoot);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field!.SetValue(target, value);
        }

        private static void InvokePrivateMethod(object target, string methodName)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method!.Invoke(target, null);
        }

        private sealed class TestTmpText : TMP_Text
        {
            protected override void LoadFontAsset()
            {
            }
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
