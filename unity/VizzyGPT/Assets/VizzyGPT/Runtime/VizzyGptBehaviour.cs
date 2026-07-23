#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ModApi.Common;
using ModApi.Ui;
using ModApi.Ui.Events;
using UnityEngine;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Storage;
using VizzyGPT.Runtime.Adapters;
using VizzyGPT.Runtime.Api;
using VizzyGPT.Runtime.Flight;
using VizzyGPT.Runtime.Security;
using VizzyGPT.Runtime.Storage;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Runtime
{
    public sealed class VizzyGptBehaviour : MonoBehaviour
    {
        private const string VizzyToolboxResourcePath = "Ui/Xml/Vizzy/VizzyToolbox";
        private const string PackagedVizzyToolboxResourcePath = "VizzyGPT/VizzyToolbox";
        private const string VizzyGptPanelResourcePath = "VizzyGPT/VizzyGptPanel";
        private const string PreviewDialogResourcePath = "VizzyGPT/PreviewDialog";
        private const string SettingsDialogResourcePath = "VizzyGPT/SettingsDialog";

        private IUserInterface? userInterface;
        private FileDataStore? store;
        private OpenAiClient? openAiClient;
        private NonSecretSettings? savedSettings;
        private string? apiKey;
        private VizzyGptPanelController? mountedPanel;
        private PreviewDialogController? mountedPreview;
        private SettingsDialogViewController? mountedSettings;
        private FlightContextCollector? flightContextCollector;
        private string? activeUserInterfaceId;

        private void Awake()
        {
            userInterface = Game.Instance == null ? null : Game.Instance.UserInterface;
            store = new FileDataStore(JunoDataPaths.Root);
            openAiClient = new OpenAiClient(new UnityWebRequestTransport());
            LoadSavedSettingsAsync();
            if (userInterface == null)
            {
                return;
            }

            userInterface.UserInterfaceLoading += OnUserInterfaceLoading;
            userInterface.UserInterfaceLoaded += OnUserInterfaceLoaded;
        }

        public static bool IsSupportedUserInterfaceId(string userInterfaceId)
        {
            return string.Equals(userInterfaceId, UserInterfaceIds.Vizzy, StringComparison.Ordinal) ||
                string.Equals(userInterfaceId, UserInterfaceIds.Flight.FlightSceneUI, StringComparison.Ordinal);
        }

        public static Transform ResolvePanelParent(GameObject? loadedUiRoot, Transform fallback)
        {
            return loadedUiRoot == null ? fallback : loadedUiRoot.transform;
        }

        public static string ResolveProgramFingerprint(string programHash)
        {
            return "program-" + programHash;
        }

        public static async Task<bool> WaitForEditorProgramAsync(
            Func<bool> tryRead,
            Func<Task> delayAsync,
            int maxAttempts)
        {
            if (tryRead == null) throw new ArgumentNullException(nameof(tryRead));
            if (delayAsync == null) throw new ArgumentNullException(nameof(delayAsync));
            if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (tryRead())
                {
                    return true;
                }

                if (attempt + 1 < maxAttempts)
                {
                    await delayAsync();
                }
            }

            return false;
        }

        private void Update()
        {
            flightContextCollector?.Tick(Time.unscaledDeltaTime);
        }

        private void OnUserInterfaceLoading(object sender, UserInterfaceLoadingEventArgs args)
        {
            if (IsSupportedUserInterfaceId(args.UserInterfaceId))
            {
                DestroyMountedUi();
            }
        }

        private void OnUserInterfaceLoaded(object sender, UserInterfaceLoadedEventArgs args)
        {
            if (userInterface == null || !IsSupportedUserInterfaceId(args.UserInterfaceId))
            {
                return;
            }

            activeUserInterfaceId = args.UserInterfaceId;
            ConfigureFlightCollector(args.UserInterfaceId);

            var panelXml = userInterface.ResourceDatabase.GetResource<TextAsset>(VizzyGptPanelResourcePath);
            if (panelXml == null)
            {
                Debug.LogWarning("VizzyGPT panel XML resource is unavailable.");
                return;
            }

            DestroyMountedUi();
            var panelParent = ResolvePanelParent(args.XmlLayout.GameObject, userInterface.Transform);
            mountedPanel = userInterface.BuildUserInterfaceFromXml<VizzyGptPanelController>(
                panelXml.text,
                "VizzyGPT.Panel",
                ConfigureMountedPanel,
                panelParent);
        }

        private void ConfigureMountedPanel(VizzyGptPanelController panel, IXmlLayoutController layoutController)
        {
            var activeStore = RequireStore();
            var adapter = new VizzyRuntimeAdapter();
            var environment = CreateWorkflowEnvironment(adapter, activeStore);
            var workflow = new VizzyGptPanelWorkflow(
                adapter,
                (request, cancellationToken) => RequireOpenAiClient().SendAsync(request, cancellationToken),
                CreateRequest,
                (fingerprint, xml, createdUtc, cancellationToken) =>
                    activeStore.SaveBackupAsync(fingerprint, xml, createdUtc, cancellationToken),
                CreateVizzyCatalog,
                () => DateTime.UtcNow,
                panel.Render,
                environment,
                environment.IsFlight
                    ? RuntimeCompatibilityResult.Compatible("Flight.Craft.FlightProgram")
                    : adapter.Compatibility);
            panel.Configure(workflow, OpenSettingsDialog, OpenPreviewDialog);
            panel.Bind(layoutController.XmlLayout);
            if (!environment.IsFlight)
            {
                RestorePendingWhenEditorReadyAsync(workflow, adapter);
            }
        }

        private VizzyGptWorkflowEnvironment CreateWorkflowEnvironment(
            VizzyRuntimeAdapter adapter,
            FileDataStore activeStore)
        {
            var isFlight = string.Equals(
                activeUserInterfaceId,
                UserInterfaceIds.Flight.FlightSceneUI,
                StringComparison.Ordinal);
            Func<string?> launchProgramXml = () =>
            {
                return isFlight && adapter.TryGetFlightProgramXml(out var xml, out _)
                    ? xml
                    : null;
            };

            return new VizzyGptWorkflowEnvironment(
                isFlight,
                ResolveProgramFingerprint,
                launchProgramXml,
                () => flightContextCollector?.Snapshot().BuildContext() ?? "FLIGHT CONTEXT\nTelemetry summaries:",
                (programFingerprint, cancellationToken) =>
                    LoadPendingWithFallbackAsync(activeStore, programFingerprint, cancellationToken),
                (pending, cancellationToken) => activeStore.SavePendingAsync(pending, cancellationToken),
                (programFingerprint, cancellationToken) =>
                    activeStore.DeletePendingAsync(programFingerprint, cancellationToken));
        }

        private static async Task<PendingChange?> LoadPendingWithFallbackAsync(
            FileDataStore activeStore,
            string programFingerprint,
            CancellationToken cancellationToken)
        {
            var exact = await activeStore.LoadPendingAsync(programFingerprint, cancellationToken);
            return exact ?? await activeStore.LoadOnlyPendingAsync(cancellationToken);
        }

        private void ConfigureFlightCollector(string userInterfaceId)
        {
            flightContextCollector?.Dispose();
            flightContextCollector = null;
            if (!string.Equals(userInterfaceId, UserInterfaceIds.Flight.FlightSceneUI, StringComparison.Ordinal))
            {
                return;
            }

            var scene = Game.Instance?.FlightScene;
            if (scene != null)
            {
                flightContextCollector = FlightContextCollector.Create(scene, () => DateTime.UtcNow);
            }
        }

        private async void RestorePendingWhenEditorReadyAsync(
            VizzyGptPanelWorkflow workflow,
            VizzyRuntimeAdapter adapter)
        {
            try
            {
                var ready = await WaitForEditorProgramAsync(
                    () => adapter.TryGetEditorProgramXml(out _, out _),
                    () => Task.Delay(100),
                    100);
                if (!ready || mountedPanel == null || !ReferenceEquals(mountedPanel.Workflow, workflow))
                {
                    return;
                }

                await workflow.RestorePendingAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("VizzyGPT could not restore a pending flight change: " + exception.Message);
            }
        }

        private AiRequest CreateRequest(string prompt, string context)
        {
            var settings = savedSettings;
            var configuredApiKey = apiKey;
            if (settings == null)
            {
                throw new InvalidOperationException("Configure and save an API endpoint before sending a request.");
            }

            if (configuredApiKey == null)
            {
                throw new InvalidOperationException("Configure and save an API endpoint before sending a request.");
            }

            if (configuredApiKey.Length == 0 || string.IsNullOrWhiteSpace(configuredApiKey))
            {
                throw new InvalidOperationException("Configure and save an API endpoint before sending a request.");
            }

            return new AiRequest(
                settings.Mode,
                prompt,
                context,
                settings.Model,
                new Uri(settings.BaseUrl, UriKind.Absolute),
                configuredApiKey,
                TimeSpan.FromSeconds(settings.TimeoutSeconds));
        }

        private VizzyNodeCatalog CreateVizzyCatalog()
        {
            var activeUserInterface = userInterface ?? throw new InvalidOperationException("The game UI is unavailable.");
            var toolbox = activeUserInterface.ResourceDatabase.GetResource<TextAsset>(VizzyToolboxResourcePath);
            if (toolbox == null || string.IsNullOrWhiteSpace(toolbox.text))
            {
                toolbox = activeUserInterface.ResourceDatabase.GetResource<TextAsset>(PackagedVizzyToolboxResourcePath);
            }

            if (toolbox == null || string.IsNullOrWhiteSpace(toolbox.text))
            {
                throw new InvalidOperationException("The stock Vizzy toolbox resource is unavailable.");
            }

            return VizzyNodeCatalog.FromToolboxXml(toolbox.text);
        }

        private void OpenPreviewDialog(PreviewDialogModel model)
        {
            if (userInterface == null || mountedPanel == null)
            {
                return;
            }

            var previewXml = userInterface.ResourceDatabase.GetResource<TextAsset>(PreviewDialogResourcePath);
            if (previewXml == null)
            {
                Debug.LogWarning("VizzyGPT preview XML resource is unavailable.");
                return;
            }

            DestroyMountedPreview();
            mountedPreview = userInterface.BuildUserInterfaceFromXml<PreviewDialogController>(
                previewXml.text,
                "VizzyGPT.Preview",
                (dialog, layoutController) =>
                {
                    dialog.Configure(RequirePanelWorkflow(), DestroyMountedPreview);
                    dialog.Bind(layoutController.XmlLayout, model);
                },
                userInterface.Transform);
        }

        private void OpenSettingsDialog()
        {
            if (userInterface == null)
            {
                return;
            }

            var settingsXml = userInterface.ResourceDatabase.GetResource<TextAsset>(SettingsDialogResourcePath);
            if (settingsXml == null)
            {
                Debug.LogWarning("VizzyGPT settings XML resource is unavailable.");
                return;
            }

            DestroyMountedSettings();
            mountedSettings = userInterface.BuildUserInterfaceFromXml<SettingsDialogViewController>(
                settingsXml.text,
                "VizzyGPT.Settings",
                (dialog, layoutController) =>
                {
                    dialog.Configure(CreateSettingsController(), LoadSavedSettingsAsync, DestroyMountedSettings);
                    dialog.Bind(layoutController.XmlLayout);
                },
                userInterface.Transform);
        }

        private SettingsDialogController CreateSettingsController()
        {
            return new SettingsDialogController(
                RequireStore(),
                DpapiSecretProtector.Protect,
                DpapiSecretProtector.Unprotect,
                (request, cancellationToken) => RequireOpenAiClient().SendAsync(request, cancellationToken));
        }

        private async void LoadSavedSettingsAsync()
        {
            try
            {
                var activeStore = RequireStore();
                var settings = await activeStore.LoadNonSecretSettingsAsync(CancellationToken.None);
                var protectedApiKey = await activeStore.LoadProtectedApiKeyAsync(CancellationToken.None);
                if (settings == null || string.IsNullOrEmpty(protectedApiKey))
                {
                    savedSettings = null;
                    apiKey = null;
                    return;
                }

                savedSettings = settings;
                apiKey = DpapiSecretProtector.Unprotect(protectedApiKey);
            }
            catch (Exception exception)
            {
                savedSettings = null;
                apiKey = null;
                Debug.LogWarning("VizzyGPT could not load saved settings: " + exception.Message);
            }
        }

        private FileDataStore RequireStore()
        {
            return store ?? throw new InvalidOperationException("VizzyGPT storage is unavailable.");
        }

        private OpenAiClient RequireOpenAiClient()
        {
            return openAiClient ?? throw new InvalidOperationException("VizzyGPT API client is unavailable.");
        }

        private VizzyGptPanelWorkflow RequirePanelWorkflow()
        {
            return mountedPanel == null
                ? throw new InvalidOperationException("VizzyGPT panel is unavailable.")
                : mountedPanel.Workflow;
        }

        private void DestroyMountedPreview()
        {
            if (mountedPreview != null)
            {
                Destroy(mountedPreview.gameObject);
                mountedPreview = null;
            }
        }

        private void DestroyMountedSettings()
        {
            if (mountedSettings != null)
            {
                Destroy(mountedSettings.gameObject);
                mountedSettings = null;
            }
        }

        private void DestroyMountedUi()
        {
            DestroyMountedPreview();
            DestroyMountedSettings();
            if (mountedPanel != null)
            {
                Destroy(mountedPanel.gameObject);
                mountedPanel = null;
            }
        }

        private void OnDestroy()
        {
            DestroyMountedUi();
            if (userInterface != null)
            {
                userInterface.UserInterfaceLoading -= OnUserInterfaceLoading;
                userInterface.UserInterfaceLoaded -= OnUserInterfaceLoaded;
                userInterface = null;
            }

            apiKey = null;
            savedSettings = null;
            openAiClient = null;
            store = null;
            flightContextCollector?.Dispose();
            flightContextCollector = null;
            activeUserInterfaceId = null;
        }
    }
}
