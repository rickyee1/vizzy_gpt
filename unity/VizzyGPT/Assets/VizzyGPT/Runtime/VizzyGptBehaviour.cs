#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using ModApi.Common;
using ModApi.Ui;
using ModApi.Ui.Events;
using UnityEngine;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Storage;
using VizzyGPT.Runtime.Adapters;
using VizzyGPT.Runtime.Api;
using VizzyGPT.Runtime.Security;
using VizzyGPT.Runtime.Storage;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Runtime
{
    public sealed class VizzyGptBehaviour : MonoBehaviour
    {
        private const string VizzyToolboxResourcePath = "Ui/Xml/Vizzy/VizzyToolbox";

        private IUserInterface? userInterface;
        private FileDataStore? store;
        private OpenAiClient? openAiClient;
        private NonSecretSettings? savedSettings;
        private string? apiKey;
        private VizzyGptPanelController? mountedPanel;
        private PreviewDialogController? mountedPreview;
        private SettingsDialogViewController? mountedSettings;

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

            var panelXml = Resources.Load<TextAsset>("Ui/VizzyGptPanel");
            if (panelXml == null)
            {
                Debug.LogWarning("VizzyGPT panel XML resource is unavailable.");
                return;
            }

            DestroyMountedUi();
            mountedPanel = userInterface.BuildUserInterfaceFromXml<VizzyGptPanelController>(
                panelXml.text,
                "VizzyGPT.Panel",
                ConfigureMountedPanel,
                userInterface.Transform);
        }

        private void ConfigureMountedPanel(VizzyGptPanelController panel, IXmlLayoutController layoutController)
        {
            var activeStore = RequireStore();
            var workflow = new VizzyGptPanelWorkflow(
                new VizzyRuntimeAdapter(),
                (request, cancellationToken) => RequireOpenAiClient().SendAsync(request, cancellationToken),
                CreateRequest,
                (fingerprint, xml, createdUtc, cancellationToken) =>
                    activeStore.SaveBackupAsync(fingerprint, xml, createdUtc, cancellationToken),
                CreateVizzyCatalog,
                () => DateTime.UtcNow,
                panel.Render);
            panel.Configure(workflow, OpenSettingsDialog, OpenPreviewDialog);
            panel.Bind(layoutController.XmlLayout);
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

            var previewXml = Resources.Load<TextAsset>("Ui/PreviewDialog");
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

            var settingsXml = Resources.Load<TextAsset>("Ui/SettingsDialog");
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
        }
    }
}
