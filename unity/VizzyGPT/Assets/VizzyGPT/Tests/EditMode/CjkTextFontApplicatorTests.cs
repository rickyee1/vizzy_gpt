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
            var text = new GameObject("text").AddComponent<TextMeshProUGUI>();
            var placeholder = new GameObject("placeholder").AddComponent<TextMeshProUGUI>();
            var font = CreateTestFontAsset();
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
                DestroyTestFontAsset(font);
            }
        }

        [Test]
        public void ApplyToText_updates_dynamic_targets_but_not_unlisted_static_label()
        {
            var font = CreateTestFontAsset();
            var transcript = new GameObject("transcript").AddComponent<TextMeshProUGUI>();
            var status = new GameObject("status").AddComponent<TextMeshProUGUI>();
            var staticLabel = new GameObject("static").AddComponent<TextMeshProUGUI>();
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
                DestroyTestFontAsset(font);
            }
        }

        [Test]
        public void Null_font_is_a_safe_no_op()
        {
            var text = new GameObject("text").AddComponent<TextMeshProUGUI>();
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

        private static TMP_FontAsset CreateTestFontAsset()
        {
            var source = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Assert.That(source, Is.Not.Null);

            var font = TMP_FontAsset.CreateFontAsset(
                source,
                32,
                4,
                UnityEngine.TextCore.LowLevel.GlyphRenderMode.SDFAA,
                1024,
                1024,
                AtlasPopulationMode.Dynamic,
                true);
            Assert.That(font, Is.Not.Null);
            return font;
        }

        private static void DestroyTestFontAsset(TMP_FontAsset? font)
        {
            if (font == null)
            {
                return;
            }

            var material = font.material;
            var atlasTextures = font.atlasTextures;
            Object.DestroyImmediate(font);

            if (material != null)
            {
                Object.DestroyImmediate(material);
            }

            if (atlasTextures == null)
            {
                return;
            }

            foreach (var atlasTexture in atlasTextures)
            {
                if (atlasTexture != null)
                {
                    Object.DestroyImmediate(atlasTexture);
                }
            }
        }
    }
}
