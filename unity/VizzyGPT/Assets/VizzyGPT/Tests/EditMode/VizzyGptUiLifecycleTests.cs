#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ModApi.Ui;
using NUnit.Framework;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Runtime;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyGptUiLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            DestroyRuntimeRoots();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyRuntimeRoots();
        }

        [Test]
        public void Staged_panel_mount_is_limited_to_vizzy_and_flight_scene_ui_ids()
        {
            Assert.That(VizzyGptBehaviour.IsSupportedUserInterfaceId(UserInterfaceIds.Vizzy), Is.True);
            Assert.That(
                VizzyGptBehaviour.IsSupportedUserInterfaceId(UserInterfaceIds.Flight.FlightSceneUI),
                Is.True);
            Assert.That(VizzyGptBehaviour.IsSupportedUserInterfaceId("VizzyGPT.UnrelatedUi"), Is.False);
        }

        [Test]
        public void Panel_mount_uses_the_loaded_ui_root_instead_of_the_global_fallback()
        {
            var loadedUiRoot = new GameObject("Flight UI Root");
            var fallbackRoot = new GameObject("Global UI Root");
            try
            {
                Assert.That(
                    VizzyGptBehaviour.ResolvePanelParent(loadedUiRoot, fallbackRoot.transform),
                    Is.SameAs(loadedUiRoot.transform));
                Assert.That(
                    VizzyGptBehaviour.ResolvePanelParent(null, fallbackRoot.transform),
                    Is.SameAs(fallbackRoot.transform));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(loadedUiRoot);
                UnityEngine.Object.DestroyImmediate(fallbackRoot);
            }
        }

        [Test]
        public void Runtime_pending_fingerprint_is_derived_consistently_from_the_program_hash()
        {
            Assert.That(
                VizzyGptBehaviour.ResolveProgramFingerprint("abc123"),
                Is.EqualTo("program-abc123"));
        }

        [Test]
        public void Pending_restore_waits_until_the_editor_program_is_initialized()
        {
            var reads = 0;

            var ready = VizzyGptBehaviour.WaitForEditorProgramAsync(
                () => ++reads >= 3,
                () => Task.CompletedTask,
                3).GetAwaiter().GetResult();

            Assert.That(ready, Is.True);
            Assert.That(reads, Is.EqualTo(3));
        }

        [Test]
        public void Behaviour_owns_the_bundled_CJK_provider_without_referencing_the_mod_entrypoint()
        {
            var source = File.ReadAllText(
                Path.Combine(Application.dataPath, "VizzyGPT/Runtime/VizzyGptBehaviour.cs"));
            var providerSetup = source.IndexOf(
                "cjkFontProvider = BundledCjkFontProvider.CreateDefault(loadFont)",
                StringComparison.Ordinal);
            var gameSetup = source.IndexOf(
                "userInterface = Game.Instance == null ? null : Game.Instance.UserInterface",
                StringComparison.Ordinal);
            var storeSetup = source.IndexOf("store = new FileDataStore", StringComparison.Ordinal);
            var clientSetup = source.IndexOf("openAiClient = new OpenAiClient", StringComparison.Ordinal);
            var settingsSetup = source.IndexOf("LoadSavedSettingsAsync()", StringComparison.Ordinal);
            var subscription = source.IndexOf(
                "userInterface.UserInterfaceLoading += OnUserInterfaceLoading",
                StringComparison.Ordinal);

            Assert.That(source, Does.Contain("Initialize(Func<string, Font?> loadFont)"));
            Assert.That(source, Does.Contain("BundledCjkFontProvider.CreateDefault(loadFont)"));
            Assert.That(source, Does.Not.Contain("Assets.Scripts"));
            Assert.That(source, Does.Not.Contain("SystemCjkFontProvider"));
            Assert.That(providerSetup, Is.GreaterThanOrEqualTo(0));
            Assert.That(providerSetup, Is.LessThan(gameSetup));
            Assert.That(gameSetup, Is.LessThan(storeSetup));
            Assert.That(storeSetup, Is.LessThan(clientSetup));
            Assert.That(clientSetup, Is.LessThan(settingsSetup));
            Assert.That(settingsSetup, Is.LessThan(subscription));
        }

        [Test]
        public void AddComponent_does_not_initialize_runtime_state_before_loader_injection()
        {
            var root = new GameObject("uninitialized-vizzy-gpt");
            try
            {
                var behaviour = root.AddComponent<VizzyGptBehaviour>();

                Assert.That(GetPrivateField(behaviour, "cjkFontProvider"), Is.Null);
                Assert.That(GetPrivateField(behaviour, "store"), Is.Null);
                Assert.That(GetPrivateField(behaviour, "openAiClient"), Is.Null);
                Assert.That(GetPrivateBool(behaviour, "initialized"), Is.False);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnsureInitialized_creates_provider_before_marking_runtime_initialized()
        {
            VizzyGptMod.EnsureInitialized(_ => null);

            var behaviour = FindRuntimeBehaviour();

            Assert.That(
                GetPrivateField(behaviour, "cjkFontProvider"),
                Is.TypeOf<BundledCjkFontProvider>());
            Assert.That(GetPrivateField(behaviour, "store"), Is.Not.Null);
            Assert.That(GetPrivateField(behaviour, "openAiClient"), Is.Not.Null);
            Assert.That(GetPrivateBool(behaviour, "initialized"), Is.True);
        }

        [Test]
        public void Duplicate_EnsureInitialized_keeps_first_provider_and_loader_path()
        {
            var firstLoadCount = 0;
            var secondLoadCount = 0;
            VizzyGptMod.EnsureInitialized(_ =>
            {
                firstLoadCount++;
                return null;
            });
            var firstBehaviour = FindRuntimeBehaviour();
            var firstProvider = (BundledCjkFontProvider)GetPrivateField(
                firstBehaviour,
                "cjkFontProvider")!;

            VizzyGptMod.EnsureInitialized(_ =>
            {
                secondLoadCount++;
                return null;
            });
            var secondBehaviour = FindRuntimeBehaviour();
            var secondProvider = (BundledCjkFontProvider)GetPrivateField(
                secondBehaviour,
                "cjkFontProvider")!;
            secondProvider.Resolve();

            Assert.That(secondBehaviour, Is.SameAs(firstBehaviour));
            Assert.That(secondProvider, Is.SameAs(firstProvider));
            Assert.That(firstLoadCount, Is.EqualTo(1));
            Assert.That(secondLoadCount, Is.Zero);
        }

        [Test]
        public void Destroying_runtime_behaviour_disposes_its_provider()
        {
            var root = new GameObject("destroyable-vizzy-gpt");
            var behaviour = root.AddComponent<VizzyGptBehaviour>();
            behaviour.Initialize(_ => null);
            var provider = (BundledCjkFontProvider)GetPrivateField(behaviour, "cjkFontProvider")!;

            try
            {
                GetPrivateMethod(behaviour, "OnDestroy").Invoke(behaviour, null);

                Assert.That(GetPrivateBool(provider, "disposed"), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Panel_bind_applies_the_cjk_font_only_to_dynamic_text()
        {
            var root = new GameObject("panel-layout", typeof(RectTransform));
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var stockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            try
            {
                var promptInput = root.AddComponent<TMP_InputField>();
                var promptText = CreateText(root.transform, "prompt-text");
                var promptPlaceholder = CreateText(root.transform, "prompt-placeholder");
                promptInput.textComponent = promptText;
                promptInput.placeholder = promptPlaceholder;
                var transcript = CreateText(root.transform, "transcript-text");
                var status = CreateText(root.transform, "status-text");
                var askButtonLabel = CreateText(root.transform, "ask-button-label");
                var originalAskButtonLabelFont = askButtonLabel.font;
                var layout = new FakeXmlLayout(root, new Dictionary<string, Component>
                {
                    ["prompt-input"] = promptInput,
                    ["transcript-text"] = transcript,
                    ["status-text"] = status,
                    ["vizzy-gpt-panel"] = (RectTransform)root.transform,
                    ["gpt-launcher-button"] = CreateComponent<Button>(root.transform, "gpt-launcher-button"),
                    ["send-button"] = CreateComponent<Button>(root.transform, "send-button"),
                    ["cancel-button"] = CreateComponent<Button>(root.transform, "cancel-button"),
                    ["preview-button"] = CreateComponent<Button>(root.transform, "preview-button"),
                    ["undo-button"] = CreateComponent<Button>(root.transform, "undo-button"),
                    ["pending-indicator"] = CreateComponent<Image>(root.transform, "pending-indicator"),
                    ["ask-toggle"] = CreateComponent<Toggle>(root.transform, "ask-toggle"),
                    ["modify-toggle"] = CreateComponent<Toggle>(root.transform, "modify-toggle")
                });
                var panel = root.AddComponent<VizzyGptPanelController>();

                panel.Bind(layout, font);

                Assert.That(promptInput.textComponent.font, Is.SameAs(font));
                Assert.That(promptPlaceholder.font, Is.SameAs(font));
                Assert.That(transcript.font, Is.SameAs(font));
                Assert.That(status.font, Is.SameAs(font));
                Assert.That(askButtonLabel.font, Is.SameAs(originalAskButtonLabelFont));
                Assert.That(askButtonLabel.font, Is.Not.SameAs(font));

                promptText.font = stockFont;
                transcript.font = stockFont;
                status.font = stockFont;
                promptInput.onValueChanged.Invoke("\u4F60");
                panel.Render(new VizzyGptPanelRenderState(
                    VizzyGptPanelMode.Ask,
                    VizzyGptPanelState.Idle,
                    "status",
                    "reply",
                    true,
                    false,
                    false,
                    false,
                    true));

                Assert.That(promptText.font, Is.SameAs(font));
                Assert.That(transcript.font, Is.SameAs(font));
                Assert.That(status.font, Is.SameAs(font));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(font);
                UnityEngine.Object.DestroyImmediate(stockFont);
            }
        }

        [Test]
        public void Panel_can_restore_dynamic_cjk_fonts_after_an_overlay_closes()
        {
            var root = new GameObject("panel-layout", typeof(RectTransform));
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var stockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            try
            {
                var promptInput = root.AddComponent<TMP_InputField>();
                var promptText = CreateText(root.transform, "prompt-text");
                var promptPlaceholder = CreateText(root.transform, "prompt-placeholder");
                promptInput.textComponent = promptText;
                promptInput.placeholder = promptPlaceholder;
                var transcript = CreateText(root.transform, "transcript-text");
                var status = CreateText(root.transform, "status-text");
                var layout = new FakeXmlLayout(root, new Dictionary<string, Component>
                {
                    ["prompt-input"] = promptInput,
                    ["transcript-text"] = transcript,
                    ["status-text"] = status,
                    ["vizzy-gpt-panel"] = (RectTransform)root.transform,
                    ["gpt-launcher-button"] = CreateComponent<Button>(root.transform, "gpt-launcher-button"),
                    ["send-button"] = CreateComponent<Button>(root.transform, "send-button"),
                    ["cancel-button"] = CreateComponent<Button>(root.transform, "cancel-button"),
                    ["preview-button"] = CreateComponent<Button>(root.transform, "preview-button"),
                    ["undo-button"] = CreateComponent<Button>(root.transform, "undo-button"),
                    ["pending-indicator"] = CreateComponent<Image>(root.transform, "pending-indicator"),
                    ["ask-toggle"] = CreateComponent<Toggle>(root.transform, "ask-toggle"),
                    ["modify-toggle"] = CreateComponent<Toggle>(root.transform, "modify-toggle")
                });
                var panel = root.AddComponent<VizzyGptPanelController>();
                panel.Bind(layout, font);

                promptText.font = stockFont;
                promptPlaceholder.font = stockFont;
                transcript.font = stockFont;
                status.font = stockFont;

                panel.RefreshDynamicTextFonts();

                Assert.That(promptText.font, Is.SameAs(font));
                Assert.That(promptPlaceholder.font, Is.SameAs(font));
                Assert.That(transcript.font, Is.SameAs(font));
                Assert.That(status.font, Is.SameAs(font));

                promptText.font = stockFont;
                promptPlaceholder.font = stockFont;
                transcript.font = stockFont;
                status.font = stockFont;

                GetPrivateMethod(panel, "LateUpdate").Invoke(panel, null);

                Assert.That(promptText.font, Is.SameAs(font));
                Assert.That(promptPlaceholder.font, Is.SameAs(font));
                Assert.That(transcript.font, Is.SameAs(font));
                Assert.That(status.font, Is.SameAs(font));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(font);
                UnityEngine.Object.DestroyImmediate(stockFont);
            }
        }

        private static TestTmpText CreateText(Transform parent, string name)
        {
            var text = new GameObject(name).AddComponent<TestTmpText>();
            text.transform.SetParent(parent);
            return text;
        }

        private static T CreateComponent<T>(Transform parent, string name) where T : Component
        {
            var component = new GameObject(name, typeof(RectTransform)).AddComponent<T>();
            component.transform.SetParent(parent);
            return component;
        }

        private static VizzyGptBehaviour FindRuntimeBehaviour()
        {
            return Resources.FindObjectsOfTypeAll<VizzyGptBehaviour>()
                .Single(behaviour => behaviour.gameObject.name == "VizzyGPT");
        }

        private static object? GetPrivateField(object instance, string fieldName)
        {
            var field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing private field '{fieldName}'.");
            return field!.GetValue(instance);
        }

        private static bool GetPrivateBool(object instance, string fieldName)
        {
            return (bool)GetPrivateField(instance, fieldName)!;
        }

        private static MethodInfo GetPrivateMethod(object instance, string methodName)
        {
            var method = instance.GetType().GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"Missing private method '{methodName}'.");
            return method!;
        }

        private static void DestroyRuntimeRoots()
        {
            var roots = Resources.FindObjectsOfTypeAll<VizzyGptBehaviour>()
                .Where(behaviour => behaviour.gameObject.name == "VizzyGPT")
                .Select(behaviour => behaviour.gameObject)
                .ToArray();
            foreach (var root in roots)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private sealed class TestTmpText : TMP_Text
        {
            protected override void LoadFontAsset()
            {
            }
        }

        private sealed class FakeXmlLayout : IXmlLayout
        {
            private readonly Dictionary<string, Component> elements;

            public FakeXmlLayout(GameObject gameObject, Dictionary<string, Component> elements)
            {
                GameObject = gameObject;
                this.elements = elements;
            }

            public GameObject GameObject { get; }

            public IXmlLayout? ParentLayout => null;

            public string Xml { get; set; } = string.Empty;

            public IXmlLayoutController? XmlLayoutController => null;

            public IXmlElement GetElementById(string id)
            {
                return null!;
            }

            public T GetElementById<T>(string id)
            {
                return elements.TryGetValue(id, out var component) && component is T typed
                    ? typed
                    : default!;
            }

            public string GetElementId(RectTransform element)
            {
                return string.Empty;
            }

            public void Hide(Action? onCompleteCallback, bool forceEvenIfNotVisible)
            {
            }

            public void RebuildLayout(bool forceEvenIfXmlUnchanged, bool throwExceptionIfXmlIsInvalid)
            {
            }

            public void Show(Action? onCompleteCallback, bool forceEvenIfVisible)
            {
            }
        }
    }
}
