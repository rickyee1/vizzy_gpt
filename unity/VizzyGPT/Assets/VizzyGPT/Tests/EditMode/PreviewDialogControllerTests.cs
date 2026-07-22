#nullable enable

using System.Reflection;
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
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
