#nullable enable

using NUnit.Framework;
using TMPro;
using UnityEngine;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class CjkTextFontApplicatorTests
    {
        [Test]
        public void ApplyToInput_updates_visible_text_and_text_placeholder()
        {
            var root = new GameObject("input");
            var text = new GameObject("text").AddComponent<TestTmpText>();
            var placeholder = new GameObject("placeholder").AddComponent<TestTmpText>();
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            try
            {
                var input = root.AddComponent<TMP_InputField>();
                text.transform.SetParent(root.transform);
                placeholder.transform.SetParent(root.transform);
                input.textComponent = text;
                input.placeholder = placeholder;

                CjkTextFontApplicator.ApplyToInput(input, font);

                Assert.That(text.font, Is.SameAs(font));
                Assert.That(placeholder.font, Is.SameAs(font));
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(font);
            }
        }

        [Test]
        public void ApplyToText_updates_dynamic_targets_but_not_unlisted_static_label()
        {
            var font = ScriptableObject.CreateInstance<TMP_FontAsset>();
            var transcript = new GameObject("transcript").AddComponent<TestTmpText>();
            var status = new GameObject("status").AddComponent<TestTmpText>();
            var staticLabel = new GameObject("static").AddComponent<TestTmpText>();
            var original = staticLabel.font;
            try
            {
                CjkTextFontApplicator.ApplyToText(font, transcript, status);

                Assert.That(transcript.font, Is.SameAs(font));
                Assert.That(status.font, Is.SameAs(font));
                Assert.That(staticLabel.font, Is.SameAs(original));
            }
            finally
            {
                Object.DestroyImmediate(transcript.gameObject);
                Object.DestroyImmediate(status.gameObject);
                Object.DestroyImmediate(staticLabel.gameObject);
                Object.DestroyImmediate(font);
            }
        }

        [Test]
        public void Null_font_is_a_safe_no_op()
        {
            var text = new GameObject("text").AddComponent<TestTmpText>();
            var original = text.font;
            try
            {
                CjkTextFontApplicator.ApplyToText(null, text);

                Assert.That(text.font, Is.SameAs(original));
            }
            finally
            {
                Object.DestroyImmediate(text.gameObject);
            }
        }

        private sealed class TestTmpText : TMP_Text
        {
            protected override void LoadFontAsset()
            {
            }
        }
    }
}
