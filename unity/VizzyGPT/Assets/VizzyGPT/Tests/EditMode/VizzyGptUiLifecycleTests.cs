#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using ModApi.Ui;
using NUnit.Framework;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Conversations;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime;
using VizzyGPT.Runtime.Adapters;
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
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var stockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            using (var fixture = new PanelFixture())
            {
                var askButtonLabel = CreateText(fixture.Root.transform, "ask-button-label");
                var originalAskButtonLabelFont = askButtonLabel.font;
                fixture.Panel.Bind(
                    fixture.Layout,
                    font,
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Panel.Render(CreateState(
                    VizzyGptPanelState.Idle,
                    new[] { Entry("user-1", ConversationRole.User, "\u4F60\u597D") }));

                Assert.That(fixture.Composer.textComponent.font, Is.SameAs(font));
                Assert.That(((TMP_Text)fixture.Composer.placeholder).font, Is.SameAs(font));
                Assert.That(
                    fixture.Content.GetComponentsInChildren<TMP_Text>(true).All(text => text.font == font),
                    Is.True);
                Assert.That(askButtonLabel.font, Is.SameAs(originalAskButtonLabelFont));
                Assert.That(askButtonLabel.font, Is.Not.SameAs(font));

                fixture.Composer.textComponent.font = stockFont;
                foreach (var text in fixture.Content.GetComponentsInChildren<TMP_Text>(true))
                {
                    text.font = stockFont;
                }

                fixture.Composer.onValueChanged.Invoke("\u4F60");
                fixture.Panel.Render(CreateState(
                    VizzyGptPanelState.Idle,
                    new[] { Entry("user-1", ConversationRole.User, "\u66F4\u65B0") }));

                Assert.That(fixture.Composer.textComponent.font, Is.SameAs(font));
                Assert.That(
                    fixture.Content.GetComponentsInChildren<TMP_Text>(true).All(text => text.font == font),
                    Is.True);
                UnityEngine.Object.DestroyImmediate(font);
                UnityEngine.Object.DestroyImmediate(stockFont);
            }
        }

        [Test]
        public void Panel_can_restore_dynamic_cjk_fonts_after_an_overlay_closes()
        {
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var stockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            using (var fixture = new PanelFixture())
            {
                fixture.Panel.Bind(
                    fixture.Layout,
                    font,
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Panel.Render(CreateState(
                    VizzyGptPanelState.Idle,
                    new[] { Entry("assistant-1", ConversationRole.Assistant, "\u56DE\u590D", reasoning: "\u601D\u8003") }));

                ReplaceDynamicFonts(fixture, stockFont);
                fixture.Panel.RefreshDynamicTextFonts();
                AssertDynamicFonts(fixture, font);

                ReplaceDynamicFonts(fixture, stockFont);
                GetPrivateMethod(fixture.Panel, "LateUpdate").Invoke(fixture.Panel, null);
                AssertDynamicFonts(fixture, font);

                UnityEngine.Object.DestroyImmediate(font);
                UnityEngine.Object.DestroyImmediate(stockFont);
            }
        }

        [Test]
        public void Panel_render_uses_structured_entries_and_swaps_send_for_cancel_in_one_position()
        {
            using (var fixture = new PanelFixture())
            {
                fixture.Panel.Bind(
                    fixture.Layout,
                    null,
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Panel.Render(CreateState(
                    VizzyGptPanelState.Idle,
                    new[] { Entry("user-1", ConversationRole.User, "hello") },
                    canSend: true));

                Assert.That(fixture.Find("message-user-1"), Is.Not.Null);
                Assert.That(fixture.Send.gameObject.activeSelf, Is.True);
                Assert.That(fixture.Cancel.gameObject.activeSelf, Is.False);

                fixture.Panel.Render(CreateState(
                    VizzyGptPanelState.Sending,
                    new[] { Entry("assistant-1", ConversationRole.Assistant, "Waiting") },
                    canCancel: true));

                Assert.That(fixture.Send.gameObject.activeSelf, Is.False);
                Assert.That(fixture.Cancel.gameObject.activeSelf, Is.True);
                Assert.That(fixture.Send.GetComponent<RectTransform>().anchoredPosition,
                    Is.EqualTo(fixture.Cancel.GetComponent<RectTransform>().anchoredPosition));
            }
        }

        [Test]
        public void Panel_bind_constrains_preferred_size_to_the_parent_canvas()
        {
            var parent = new GameObject("small-canvas", typeof(RectTransform));
            ((RectTransform)parent.transform).sizeDelta = new Vector2(320, 480);
            try
            {
                using (var fixture = new PanelFixture())
                {
                    fixture.Root.transform.SetParent(parent.transform, false);
                    ((RectTransform)fixture.Root.transform).sizeDelta = new Vector2(500, 700);

                    fixture.Panel.Bind(
                        fixture.Layout,
                        null,
                        gameObject => gameObject.AddComponent<TestTmpText>());

                    Assert.That(((RectTransform)fixture.Root.transform).rect.width, Is.LessThanOrEqualTo(288));
                    Assert.That(((RectTransform)fixture.Root.transform).rect.height, Is.LessThanOrEqualTo(448));
                    Assert.That(fixture.Composer.GetComponent<RectTransform>().rect.width, Is.LessThanOrEqualTo(256));
                    Assert.That(fixture.ModePanel.rect.width, Is.LessThanOrEqualTo(256));
                    AssertResponsiveModeGeometry(fixture);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Panel_update_tracks_parent_resize_and_restores_preferred_geometry()
        {
            var parent = new GameObject("resizable-canvas", typeof(RectTransform));
            var parentRect = (RectTransform)parent.transform;
            parentRect.sizeDelta = new Vector2(900, 900);
            try
            {
                using (var fixture = new PanelFixture())
                {
                    fixture.Root.transform.SetParent(parent.transform, false);
                    fixture.Panel.Bind(
                        fixture.Layout,
                        null,
                        gameObject => gameObject.AddComponent<TestTmpText>());

                    AssertPreferredPanelGeometry(fixture);

                    parentRect.sizeDelta = new Vector2(320, 480);
                    GetPrivateMethod(fixture.Panel, "Update").Invoke(fixture.Panel, null);

                    Assert.That(((RectTransform)fixture.Root.transform).rect.width, Is.EqualTo(288f).Within(0.01f));
                    Assert.That(((RectTransform)fixture.Root.transform).rect.height, Is.EqualTo(448f).Within(0.01f));
                    Assert.That(fixture.Composer.GetComponent<RectTransform>().rect.width, Is.EqualTo(256f).Within(0.01f));
                    Assert.That(fixture.ModePanel.rect.width, Is.EqualTo(256f).Within(0.01f));
                    AssertResponsiveModeGeometry(fixture);

                    parentRect.sizeDelta = new Vector2(900, 900);
                    GetPrivateMethod(fixture.Panel, "Update").Invoke(fixture.Panel, null);

                    AssertPreferredPanelGeometry(fixture);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Panel_clears_composer_when_send_is_accepted_but_not_when_prompt_is_blank()
        {
            var completion = new TaskCompletionSource<AiResponse>();
            using (var fixture = new PanelFixture())
            {
                fixture.Panel.Bind(
                    fixture.Layout,
                    null,
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Panel.Configure(CreateWorkflow((_, __) => completion.Task));
                fixture.Panel.Workflow.OpenPanel();

                fixture.Panel.SetPromptText("   ");
                fixture.Composer.text = "   ";
                fixture.Panel.OnSendButtonClicked();
                Assert.That(fixture.Composer.text, Is.EqualTo("   "));

                fixture.Panel.SetPromptText("Explain this");
                fixture.Composer.text = "Explain this";
                fixture.Panel.OnSendButtonClicked();

                Assert.That(fixture.Panel.Workflow.State, Is.EqualTo(VizzyGptPanelState.Sending));
                Assert.That(fixture.Composer.text, Is.Empty);

                completion.SetResult(new AiResponse("Done.", null, false, Array.Empty<string>()));
                Assert.That(fixture.Panel.Workflow.State, Is.EqualTo(VizzyGptPanelState.Idle));
            }
        }

        private static VizzyGptPanelRenderState CreateState(
            VizzyGptPanelState state,
            IReadOnlyList<ConversationEntryRenderModel> entries,
            bool canSend = false,
            bool canCancel = false)
        {
            return new VizzyGptPanelRenderState(
                VizzyGptPanelMode.Ask,
                state,
                string.Empty,
                string.Empty,
                entries,
                canSend,
                canCancel,
                entries.Any(entry => entry.CanPreview),
                false,
                true);
        }

        private static ConversationEntryRenderModel Entry(
            string id,
            ConversationRole role,
            string text,
            string? reasoning = null)
        {
            return new ConversationEntryRenderModel(
                id,
                role,
                text,
                reasoning,
                Array.Empty<ConversationStageTiming>(),
                null,
                null,
                null,
                false);
        }

        private static VizzyGptPanelWorkflow CreateWorkflow(
            Func<AiRequest, CancellationToken, Task<AiResponse>> sendAsync)
        {
            return new VizzyGptPanelWorkflow(
                new FakeAdapter(),
                sendAsync,
                (prompt, context) => new AiRequest(
                    ApiMode.Auto,
                    prompt,
                    context,
                    "test-model",
                    new Uri("https://api.example.test"),
                    "test-key",
                    TimeSpan.FromSeconds(10)),
                (_, __, ___, ____) => Task.CompletedTask,
                () => VizzyNodeCatalog.FromToolboxXml(
                    "<VizzyToolbox><Instructions><Log /></Instructions><Expressions /></VizzyToolbox>"),
                () => new DateTime(2026, 7, 29, 0, 0, 0, DateTimeKind.Utc),
                _ => { });
        }

        private static void ReplaceDynamicFonts(PanelFixture fixture, TMP_FontAsset font)
        {
            fixture.Composer.textComponent.font = font;
            ((TMP_Text)fixture.Composer.placeholder).font = font;
            foreach (var text in fixture.Content.GetComponentsInChildren<TMP_Text>(true))
            {
                text.font = font;
            }
        }

        private static void AssertDynamicFonts(PanelFixture fixture, TMP_FontAsset font)
        {
            Assert.That(fixture.Composer.textComponent.font, Is.SameAs(font));
            Assert.That(((TMP_Text)fixture.Composer.placeholder).font, Is.SameAs(font));
            Assert.That(
                fixture.Content.GetComponentsInChildren<TMP_Text>(true).All(text => text.font == font),
                Is.True);
        }

        private static TestTmpText CreateText(Transform parent, string name)
        {
            var text = new GameObject(name).AddComponent<TestTmpText>();
            text.transform.SetParent(parent);
            return text;
        }

        private static void AssertPreferredPanelGeometry(PanelFixture fixture)
        {
            Assert.That(((RectTransform)fixture.Root.transform).rect.width, Is.EqualTo(500f).Within(0.01f));
            Assert.That(((RectTransform)fixture.Root.transform).rect.height, Is.EqualTo(700f).Within(0.01f));
            Assert.That(fixture.Composer.GetComponent<RectTransform>().rect.width, Is.EqualTo(468f).Within(0.01f));
            Assert.That(fixture.ModePanel.rect.width, Is.EqualTo(468f).Within(0.01f));
            Assert.That(fixture.ModeToggleGroup.rect.width, Is.EqualTo(280f).Within(0.01f));
            Assert.That(fixture.AskToggle.rect.width, Is.EqualTo(138f).Within(0.01f));
            Assert.That(fixture.ModifyToggle.rect.width, Is.EqualTo(138f).Within(0.01f));
            AssertResponsiveModeGeometry(fixture);
        }

        private static void AssertResponsiveModeGeometry(PanelFixture fixture)
        {
            var labelBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                fixture.ModePanel,
                fixture.ModeLabel);
            var groupBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                fixture.ModePanel,
                fixture.ModeToggleGroup);
            var askBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                fixture.ModeToggleGroup,
                fixture.AskToggle);
            var modifyBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                fixture.ModeToggleGroup,
                fixture.ModifyToggle);

            Assert.That(groupBounds.min.x, Is.GreaterThanOrEqualTo(labelBounds.max.x + 12f - 0.01f));
            Assert.That(groupBounds.min.x, Is.GreaterThanOrEqualTo(fixture.ModePanel.rect.xMin - 0.01f));
            Assert.That(groupBounds.max.x, Is.LessThanOrEqualTo(fixture.ModePanel.rect.xMax + 0.01f));
            Assert.That(askBounds.min.x, Is.GreaterThanOrEqualTo(fixture.ModeToggleGroup.rect.xMin - 0.01f));
            Assert.That(modifyBounds.max.x, Is.LessThanOrEqualTo(fixture.ModeToggleGroup.rect.xMax + 0.01f));
            Assert.That(askBounds.max.x, Is.LessThanOrEqualTo(modifyBounds.min.x + 0.01f));
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

        private sealed class PanelFixture : IDisposable
        {
            public PanelFixture()
            {
                Root = new GameObject("panel-layout", typeof(RectTransform));
                ((RectTransform)Root.transform).sizeDelta = new Vector2(500, 700);
                Composer = new GameObject("composer-input", typeof(RectTransform)).AddComponent<TMP_InputField>();
                Composer.transform.SetParent(Root.transform, false);
                Composer.GetComponent<RectTransform>().sizeDelta = new Vector2(468, 88);
                var composerText = CreateText(Composer.transform, "composer-text");
                var composerPlaceholder = CreateText(Composer.transform, "composer-placeholder");
                Composer.textComponent = composerText;
                Composer.placeholder = composerPlaceholder;

                var viewport = new GameObject("conversation-scroll", typeof(RectTransform), typeof(ScrollRect));
                viewport.transform.SetParent(Root.transform, false);
                Scroll = viewport.GetComponent<ScrollRect>();
                Content = (RectTransform)new GameObject("conversation-content", typeof(RectTransform)).transform;
                Content.SetParent(viewport.transform, false);
                Scroll.viewport = (RectTransform)viewport.transform;
                Scroll.content = Content;
                Send = CreateComponent<Button>(Root.transform, "send-button");
                Cancel = CreateComponent<Button>(Root.transform, "cancel-request-button");
                Send.GetComponent<RectTransform>().anchoredPosition = new Vector2(-12, 0);
                Cancel.GetComponent<RectTransform>().anchoredPosition = new Vector2(-12, 0);
                ModePanel = (RectTransform)new GameObject("mode-panel", typeof(RectTransform)).transform;
                ModePanel.SetParent(Root.transform, false);
                ModePanel.sizeDelta = new Vector2(468, 48);
                ModeLabel = (RectTransform)new GameObject("mode-label", typeof(RectTransform)).transform;
                ModeLabel.SetParent(ModePanel, false);
                ConfigureRect(ModeLabel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f), 80f, 20f);
                ModeToggleGroup = (RectTransform)new GameObject("mode-toggle-group", typeof(RectTransform)).transform;
                ModeToggleGroup.SetParent(ModePanel, false);
                ConfigureRect(ModeToggleGroup, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), 280f, 25f);
                AskToggle = CreateComponent<Toggle>(ModeToggleGroup, "ask-toggle").GetComponent<RectTransform>();
                ConfigureRect(AskToggle, Vector2.zero, Vector2.zero, Vector2.zero, 138f, 25f);
                ModifyToggle = CreateComponent<Toggle>(ModeToggleGroup, "modify-toggle").GetComponent<RectTransform>();
                ConfigureRect(ModifyToggle, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), 138f, 25f);

                Layout = new FakeXmlLayout(Root, new Dictionary<string, Component>
                {
                    ["composer-input"] = Composer,
                    ["conversation-scroll"] = Scroll,
                    ["conversation-content"] = Content,
                    ["vizzy-gpt-panel"] = (RectTransform)Root.transform,
                    ["mode-panel"] = ModePanel,
                    ["mode-label"] = ModeLabel,
                    ["mode-toggle-group"] = ModeToggleGroup,
                    ["gpt-launcher-button"] = CreateComponent<Button>(Root.transform, "gpt-launcher-button"),
                    ["send-button"] = Send,
                    ["cancel-request-button"] = Cancel,
                    ["undo-button"] = CreateComponent<Button>(Root.transform, "undo-button"),
                    ["ask-toggle"] = AskToggle.GetComponent<Toggle>(),
                    ["modify-toggle"] = ModifyToggle.GetComponent<Toggle>()
                });
                Panel = Root.AddComponent<VizzyGptPanelController>();
            }

            public GameObject Root { get; }
            public TMP_InputField Composer { get; }
            public RectTransform Content { get; }
            public RectTransform ModePanel { get; }
            public RectTransform ModeLabel { get; }
            public RectTransform ModeToggleGroup { get; }
            public RectTransform AskToggle { get; }
            public RectTransform ModifyToggle { get; }
            public ScrollRect Scroll { get; }
            public Button Send { get; }
            public Button Cancel { get; }
            public FakeXmlLayout Layout { get; }
            public VizzyGptPanelController Panel { get; }

            public GameObject? Find(string name)
            {
                return Content.GetComponentsInChildren<Transform>(true)
                    .Select(item => item.gameObject)
                    .FirstOrDefault(item => item.name == name);
            }

            public void Dispose()
            {
                UnityEngine.Object.DestroyImmediate(Root);
            }

            private static void ConfigureRect(
                RectTransform rect,
                Vector2 anchorMin,
                Vector2 anchorMax,
                Vector2 pivot,
                float width,
                float height)
            {
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(width, height);
            }
        }

        private sealed class FakeAdapter : IVizzyRuntimeAdapter
        {
            private const string ProgramXml =
                "<Program><Variables /><Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>";

            public bool IsEditorAvailable => true;

            public bool IsFlightAvailable => false;

            public bool TryGetEditorProgramXml(out string xml, out string error)
            {
                xml = ProgramXml;
                error = string.Empty;
                return true;
            }

            public bool TrySetEditorProgramXml(string xml, out string error)
            {
                error = string.Empty;
                return true;
            }

            public bool TryGetFlightProgramXml(out string xml, out string error)
            {
                xml = string.Empty;
                error = "Unavailable.";
                return false;
            }

            public ValidationIssue? ValidateWithProgramSerializer(string xml)
            {
                return null;
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
