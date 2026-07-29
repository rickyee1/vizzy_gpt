#nullable enable

using System;
using System.Globalization;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using ModApi.Ui;
using TMPro;
using UnityEngine;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Storage;
using VizzyGPT.Runtime.Security;

namespace VizzyGPT.Runtime.Ui
{
    public sealed class VizzyGptSettingsDraft
    {
        public VizzyGptSettingsDraft(string baseUrl, ApiMode mode, string model, string apiKey, int timeoutSeconds)
        {
            BaseUrl = baseUrl ?? throw new ArgumentNullException(nameof(baseUrl));
            Mode = mode;
            Model = model ?? throw new ArgumentNullException(nameof(model));
            ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey));
            TimeoutSeconds = timeoutSeconds;
        }

        public string BaseUrl { get; }

        public ApiMode Mode { get; }

        public string Model { get; }

        public string ApiKey { get; }

        public int TimeoutSeconds { get; }
    }

    public sealed class SettingsConnectionResult
    {
        public SettingsConnectionResult(bool success, string status)
        {
            Success = success;
            Status = status ?? throw new ArgumentNullException(nameof(status));
        }

        public bool Success { get; }

        public string Status { get; }
    }

    public sealed class SettingsDialogController
    {
        private readonly IDataStore? configuredStore;
        private readonly Func<string, string>? protect;
        private readonly Func<string, string>? unprotect;
        private readonly Func<AiRequest, CancellationToken, Task<AiResponse>>? testConnectionAsync;
        private CancellationTokenSource? connectionCancellation;

        public SettingsDialogController(
            IDataStore store,
            Func<string, string> protect,
            Func<string, string> unprotect,
            Func<AiRequest, CancellationToken, Task<AiResponse>> testConnectionAsync)
        {
            configuredStore = store ?? throw new ArgumentNullException(nameof(store));
            this.protect = protect ?? throw new ArgumentNullException(nameof(protect));
            this.unprotect = unprotect ?? throw new ArgumentNullException(nameof(unprotect));
            this.testConnectionAsync = testConnectionAsync ?? throw new ArgumentNullException(nameof(testConnectionAsync));
        }

        public string DestinationHost { get; private set; } = string.Empty;

        public async Task SaveAsync(VizzyGptSettingsDraft draft, CancellationToken cancellationToken = default)
        {
            var store = RequireStore();
            var request = BuildRequest(draft);
            var settings = new NonSecretSettings(
                request.BaseUri.AbsoluteUri,
                draft.Mode,
                draft.Model,
                draft.TimeoutSeconds);
            var protectedApiKey = RequireProtect()(draft.ApiKey);

            await store.SaveNonSecretSettingsAsync(settings, cancellationToken).ConfigureAwait(false);
            await store.SaveProtectedApiKeyAsync(protectedApiKey, cancellationToken).ConfigureAwait(false);
            DestinationHost = request.BaseUri.Host;
        }

        public async Task<VizzyGptSettingsDraft?> LoadAsync(CancellationToken cancellationToken = default)
        {
            var store = RequireStore();
            var settings = await store.LoadNonSecretSettingsAsync(cancellationToken).ConfigureAwait(false);
            if (settings == null)
            {
                return null;
            }

            var protectedApiKey = await store.LoadProtectedApiKeyAsync(cancellationToken).ConfigureAwait(false);
            if (protectedApiKey == null)
            {
                return null;
            }

            var apiKey = RequireUnprotect()(protectedApiKey);
            var draft = new VizzyGptSettingsDraft(
                settings.BaseUrl,
                settings.Mode,
                settings.Model,
                apiKey,
                settings.TimeoutSeconds);
            DestinationHost = BuildRequest(draft).BaseUri.Host;
            return draft;
        }

        public async Task<SettingsConnectionResult> TestConnectionAsync(
            VizzyGptSettingsDraft draft,
            CancellationToken cancellationToken = default)
        {
            CancelTestConnection();
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectionCancellation = cancellation;
            try
            {
                var response = await RequireTestConnection()(BuildRequest(draft), cancellation.Token).ConfigureAwait(false);
                DestinationHost = BuildRequest(draft).BaseUri.Host;
                return new SettingsConnectionResult(true, response.Message);
            }
            catch (OperationCanceledException)
            {
                return new SettingsConnectionResult(false, "Connection test cancelled.");
            }
            catch (Exception exception)
            {
                return new SettingsConnectionResult(false, exception.Message);
            }
            finally
            {
                cancellation.Dispose();
                if (ReferenceEquals(connectionCancellation, cancellation))
                {
                    connectionCancellation = null;
                }
            }
        }

        public void CancelTestConnection()
        {
            connectionCancellation?.Cancel();
        }

        private static AiRequest BuildRequest(VizzyGptSettingsDraft draft)
        {
            if (draft == null)
            {
                throw new ArgumentNullException(nameof(draft));
            }

            return new AiRequest(
                draft.Mode,
                "Connection test. Return a minimal valid response.",
                "CONNECTION TEST\nNo program XML is included.",
                draft.Model,
                new Uri(draft.BaseUrl, UriKind.Absolute),
                draft.ApiKey,
                TimeSpan.FromSeconds(draft.TimeoutSeconds));
        }

        private IDataStore RequireStore()
        {
            return configuredStore ?? throw new InvalidOperationException("Settings store is not configured.");
        }

        private Func<string, string> RequireProtect()
        {
            return protect ?? throw new InvalidOperationException("DPAPI protector is not configured.");
        }

        private Func<string, string> RequireUnprotect()
        {
            return unprotect ?? throw new InvalidOperationException("DPAPI unprotector is not configured.");
        }

        private Func<AiRequest, CancellationToken, Task<AiResponse>> RequireTestConnection()
        {
            return testConnectionAsync ?? throw new InvalidOperationException("Connection client is not configured.");
        }
    }

    public sealed class SettingsDialogViewController : MonoBehaviour
    {
        private SettingsDialogController? controller;
        private Action? settingsSaved;
        private Action? close;
        private TMP_InputField? baseUrlInput;
        private TMP_InputField? apiModeInput;
        private TMP_InputField? modelInput;
        private TMP_InputField? apiKeyInput;
        private TMP_InputField? timeoutInput;
        private TMP_Text? destinationHostText;
        private TMP_Text? connectionStatusText;
        private bool disposed;

        public void Configure(SettingsDialogController value, Action onSettingsSaved, Action closeAction)
        {
            controller = value ?? throw new ArgumentNullException(nameof(value));
            settingsSaved = onSettingsSaved ?? throw new ArgumentNullException(nameof(onSettingsSaved));
            close = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        }

        public void Bind(IXmlLayout layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            baseUrlInput = RequireElement<TMP_InputField>(layout, "base-url-input");
            apiModeInput = RequireElement<TMP_InputField>(layout, "api-mode-input");
            modelInput = RequireElement<TMP_InputField>(layout, "model-input");
            apiKeyInput = RequireElement<TMP_InputField>(layout, "api-key-input");
            timeoutInput = RequireElement<TMP_InputField>(layout, "timeout-input");
            destinationHostText = RequireElement<TMP_Text>(layout, "destination-host-text");
            connectionStatusText = RequireElement<TMP_Text>(layout, "connection-status-text");
            if (baseUrlInput != null)
            {
                baseUrlInput.onValueChanged.AddListener(OnBaseUrlChanged);
                RenderDestinationFromBaseUrl(baseUrlInput.text);
            }

            LoadSavedDraftAsync();
        }

        public async void OnSaveButtonClicked()
        {
            try
            {
                var activeController = RequireController();
                await activeController.SaveAsync(ReadDraft());
                RenderDestination(activeController.DestinationHost);
                settingsSaved?.Invoke();
                close?.Invoke();
            }
            catch (Exception exception)
            {
                RenderStatus(exception.Message);
            }
        }

        public async void OnTestConnectionButtonClicked()
        {
            try
            {
                var activeController = RequireController();
                var result = await activeController.TestConnectionAsync(ReadDraft());
                if (disposed || !ReferenceEquals(controller, activeController))
                {
                    return;
                }

                RenderDestination(activeController.DestinationHost);
                RenderStatus(result.Status);
            }
            catch (Exception exception)
            {
                RenderStatus(exception.Message);
            }
        }

        public void OnCancelButtonClicked()
        {
            controller?.CancelTestConnection();
            close?.Invoke();
        }

        private async void LoadSavedDraftAsync()
        {
            try
            {
                var draft = await RequireController().LoadAsync();
                if (draft == null)
                {
                    return;
                }

                if (baseUrlInput != null) baseUrlInput.text = draft.BaseUrl;
                if (apiModeInput != null) apiModeInput.text = draft.Mode.ToString();
                if (modelInput != null) modelInput.text = draft.Model;
                if (apiKeyInput != null) apiKeyInput.text = draft.ApiKey;
                if (timeoutInput != null) timeoutInput.text = draft.TimeoutSeconds.ToString(CultureInfo.InvariantCulture);
                RenderDestination(RequireController().DestinationHost);
            }
            catch (Exception exception)
            {
                RenderStatus(exception.Message);
            }
        }

        private VizzyGptSettingsDraft ReadDraft()
        {
            if (!Enum.TryParse(apiModeInput == null ? string.Empty : apiModeInput.text, true, out ApiMode mode))
            {
                throw new InvalidOperationException("API mode must be Auto, Responses, or ChatCompletions.");
            }

            if (!int.TryParse(timeoutInput == null ? string.Empty : timeoutInput.text, NumberStyles.None, CultureInfo.InvariantCulture, out var timeout) || timeout <= 0)
            {
                throw new InvalidOperationException("Timeout seconds must be a positive integer.");
            }

            return new VizzyGptSettingsDraft(
                baseUrlInput == null ? string.Empty : baseUrlInput.text,
                mode,
                modelInput == null ? string.Empty : modelInput.text,
                apiKeyInput == null ? string.Empty : apiKeyInput.text,
                timeout);
        }

        private void RenderDestination(string destinationHost)
        {
            if (destinationHostText != null)
            {
                destinationHostText.text = "Destination: " + Escape(destinationHost);
            }
        }

        private void OnBaseUrlChanged(string value)
        {
            RenderDestinationFromBaseUrl(value);
        }

        private void RenderDestinationFromBaseUrl(string value)
        {
            if (Uri.TryCreate(value, UriKind.Absolute, out var destination))
            {
                RenderDestination(destination.Host);
                return;
            }

            RenderDestination(string.Empty);
        }

        private void RenderStatus(string status)
        {
            if (connectionStatusText != null)
            {
                connectionStatusText.text = Escape(status);
            }
        }

        private SettingsDialogController RequireController()
        {
            return controller ?? throw new InvalidOperationException("Settings dialog is not configured.");
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private static T RequireElement<T>(IXmlLayout layout, string id) where T : Component
        {
            return layout.GetElementById<T>(id) ??
                throw new InvalidOperationException("Vizzy GPT settings XML is missing required " + typeof(T).Name + " '" + id + "'.");
        }

        private void OnDestroy()
        {
            disposed = true;
            controller?.CancelTestConnection();
            if (baseUrlInput != null)
            {
                baseUrlInput.onValueChanged.RemoveListener(OnBaseUrlChanged);
            }

            controller = null;
            settingsSaved = null;
            close = null;
        }
    }
}
