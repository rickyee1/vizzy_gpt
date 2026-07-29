#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using ModApi.Ui;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class PreviewDialogControllerTests
    {
        [Test]
        public void Runtime_preview_text_preserves_apostrophes_and_disables_rich_text()
        {
            var gameObject = new GameObject("preview-text-test");
            try
            {
                LogAssert.ignoreFailingMessages = true;
                var text = gameObject.AddComponent<TextMeshProUGUI>();
                text.richText = true;
                var method = typeof(PreviewDialogController).GetMethod(
                    "SetText",
                    BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new[] { typeof(TMP_Text), typeof(string) },
                    null);

                Assert.That(method, Is.Not.Null);
                method!.Invoke(null, new object[] { text, "Added variable 'gpt_loopback' <test>." });

                Assert.That(text.text, Is.EqualTo("Added variable 'gpt_loopback' <test>."));
                Assert.That(text.richText, Is.False);
            }
            finally
            {
                LogAssert.ignoreFailingMessages = false;
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void Preview_bind_applies_the_cjk_font_only_to_dynamic_text()
        {
            var root = new GameObject("preview-layout");
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var stockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
            try
            {
                var summary = CreateText(root.transform, "summary-text");
                var added = CreateText(root.transform, "added-text");
                var changed = CreateText(root.transform, "changed-text");
                var removed = CreateText(root.transform, "removed-text");
                var warnings = CreateText(root.transform, "warnings-text");
                var applyButtonLabel = CreateText(root.transform, "apply-button-label");
                var originalApplyButtonLabelFont = applyButtonLabel.font;
                var layout = new FakeXmlLayout(root, new Dictionary<string, Component>
                {
                    ["summary-text"] = summary,
                    ["added-text"] = added,
                    ["changed-text"] = changed,
                    ["removed-text"] = removed,
                    ["warnings-text"] = warnings
                });
                var controller = root.AddComponent<PreviewDialogController>();

                controller.Bind(layout, CreateModel(), font);

                Assert.That(summary.font, Is.SameAs(font));
                Assert.That(added.font, Is.SameAs(font));
                Assert.That(changed.font, Is.SameAs(font));
                Assert.That(removed.font, Is.SameAs(font));
                Assert.That(warnings.font, Is.SameAs(font));
                Assert.That(applyButtonLabel.font, Is.SameAs(originalApplyButtonLabelFont));
                Assert.That(applyButtonLabel.font, Is.Not.SameAs(font));

                summary.font = stockFont;
                added.font = stockFont;
                changed.font = stockFont;
                removed.font = stockFont;
                warnings.font = stockFont;
                var lateUpdate = typeof(PreviewDialogController).GetMethod(
                    "LateUpdate",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That(lateUpdate, Is.Not.Null);
                lateUpdate!.Invoke(controller, Array.Empty<object>());

                Assert.That(summary.font, Is.SameAs(font));
                Assert.That(added.font, Is.SameAs(font));
                Assert.That(changed.font, Is.SameAs(font));
                Assert.That(removed.font, Is.SameAs(font));
                Assert.That(warnings.font, Is.SameAs(font));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(font);
                UnityEngine.Object.DestroyImmediate(stockFont);
            }
        }

        private static PreviewDialogModel CreateModel()
        {
            var constructor = typeof(PreviewDialogModel).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[]
                {
                    typeof(string),
                    typeof(IReadOnlyList<string>),
                    typeof(IReadOnlyList<string>),
                    typeof(IReadOnlyList<string>),
                    typeof(IReadOnlyList<string>)
                },
                null);

            Assert.That(constructor, Is.Not.Null);
            return (PreviewDialogModel)constructor!.Invoke(new object[]
            {
                "Summary",
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>()
            });
        }

        private static TestTmpText CreateText(Transform parent, string name)
        {
            var text = new GameObject(name).AddComponent<TestTmpText>();
            text.transform.SetParent(parent);
            return text;
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
