#nullable enable

using System;
using System.Collections.Generic;
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
        public void Panel_bind_applies_the_cjk_font_only_to_dynamic_text()
        {
            var root = new GameObject("panel-layout", typeof(RectTransform));
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
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
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(font);
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
