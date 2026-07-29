#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Core.Conversations;
using VizzyGPT.Runtime.Ui;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class ConversationMessageListViewTests
    {
        [Test]
        public void Render_creates_stable_rows_with_role_body_meta_reasoning_error_and_contextual_preview()
        {
            using (var fixture = new ViewFixture())
            {
                var previewCount = 0;
                var entries = new[]
                {
                    Entry("user-1", ConversationRole.User, "\u4F60\u597D"),
                    Entry(
                        "assistant-1",
                        ConversationRole.Assistant,
                        "\u5DF2\u5B8C\u6210",
                        reasoning: "\u7528\u6237\u8981\u6C42\u6458\u8981",
                        stages: new[] { new ConversationStageTiming("WaitingForModel", 1.25) },
                        error: new ConversationError(
                            "invalid_patch",
                            "ValidatingPatch",
                            "\u65E0\u6CD5\u9A8C\u8BC1\u66F4\u6539",
                            "Protected root: Program",
                            "/Program"),
                        canPreview: true)
                };

                fixture.View = new ConversationMessageListView(
                    fixture.Content,
                    fixture.Scroll,
                    fixture.Composer,
                    fixture.Font,
                    () => previewCount++,
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.View.Render(entries);

                var userRow = fixture.Find("message-user-1");
                var assistantRow = fixture.Find("message-assistant-1");
                Assert.That(userRow.GetComponent<Image>(), Is.Not.Null);
                Assert.That(assistantRow.GetComponent<Image>(), Is.Null);
                Assert.That(fixture.Text("message-user-1-role").text, Is.EqualTo("You"));
                Assert.That(fixture.Text("message-user-1-body").text, Is.EqualTo("\u4F60\u597D"));
                Assert.That(fixture.Text("message-assistant-1-role").text, Is.EqualTo("Vizzy GPT"));
                Assert.That(fixture.Text("message-assistant-1-body").text, Is.EqualTo("\u5DF2\u5B8C\u6210"));
                Assert.That(fixture.Text("message-assistant-1-meta").text, Does.Contain("1.3s"));
                Assert.That(fixture.Find("message-assistant-1-reasoning-detail").activeSelf, Is.False);
                Assert.That(fixture.Find("message-assistant-1-error-detail").activeSelf, Is.False);

                var composerSize = fixture.Composer.sizeDelta;
                fixture.Button("message-assistant-1-reasoning-button").onClick.Invoke();
                fixture.Button("message-assistant-1-error-button").onClick.Invoke();
                fixture.Button("message-assistant-1-preview-button").onClick.Invoke();

                Assert.That(fixture.Find("message-assistant-1-reasoning-detail").activeSelf, Is.True);
                Assert.That(fixture.Text("message-assistant-1-reasoning-detail").text, Does.Contain("\u7528\u6237\u8981\u6C42\u6458\u8981"));
                Assert.That(fixture.Find("message-assistant-1-error-detail").activeSelf, Is.True);
                Assert.That(fixture.Text("message-assistant-1-error-detail").text, Does.Contain("Protected root: Program"));
                Assert.That(fixture.Composer.sizeDelta, Is.EqualTo(composerSize));
                Assert.That(previewCount, Is.EqualTo(1));

                fixture.View.Render(new[]
                {
                    Entry("assistant-1", ConversationRole.Assistant, "\u66F4\u65B0\u540E", reasoning: "\u7528\u6237\u8981\u6C42\u6458\u8981")
                });

                Assert.That(fixture.Find("message-assistant-1"), Is.SameAs(assistantRow));
                Assert.That(fixture.FindOrNull("message-user-1"), Is.Null);
                Assert.That(fixture.Find("message-assistant-1-reasoning-detail").activeSelf, Is.True);
            }
        }

        [Test]
        public void Render_preserves_manual_scroll_and_refresh_restores_every_dynamic_font()
        {
            using (var fixture = new ViewFixture())
            {
                fixture.View = new ConversationMessageListView(
                    fixture.Content,
                    fixture.Scroll,
                    fixture.Composer,
                    fixture.Font,
                    () => { },
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Content.anchoredPosition = new Vector2(0, 200);
                fixture.Scroll.Rebuild(CanvasUpdate.PostLayout);
                var manualPosition = fixture.Scroll.verticalNormalizedPosition;
                Assert.That(manualPosition, Is.GreaterThan(0.05f));
                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "\u4F60\u597D"),
                    Entry("assistant-1", ConversationRole.Assistant, "\u5DF2\u5B8C\u6210", reasoning: "\u601D\u8003")
                });

                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.EqualTo(manualPosition).Within(0.001f));
                var dynamicTexts = fixture.Content.GetComponentsInChildren<TMP_Text>(true);
                Assert.That(dynamicTexts, Is.Not.Empty);
                Assert.That(dynamicTexts.All(text => text.font == fixture.Font), Is.True);

                foreach (var text in dynamicTexts)
                {
                    text.font = fixture.StockFont;
                }

                fixture.View.RefreshDynamicTextFonts();

                Assert.That(dynamicTexts.All(text => text.font == fixture.Font), Is.True);
            }
        }

        [Test]
        public void Render_scrolls_first_message_and_bottom_completion_to_the_bottom()
        {
            using (var fixture = new ViewFixture())
            {
                fixture.Content.sizeDelta = new Vector2(0, 0);
                fixture.View = new ConversationMessageListView(
                    fixture.Content,
                    fixture.Scroll,
                    fixture.Composer,
                    fixture.Font,
                    () => { },
                    gameObject => gameObject.AddComponent<TestTmpText>());
                fixture.Scroll.verticalNormalizedPosition = 1f;

                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "hello")
                });

                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.Zero);

                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "hello"),
                    Entry("assistant-1", ConversationRole.Assistant, "done")
                });

                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.Zero);
            }
        }

        private static ConversationEntryRenderModel Entry(
            string id,
            ConversationRole role,
            string text,
            string? reasoning = null,
            IReadOnlyList<ConversationStageTiming>? stages = null,
            RequestStage? currentStage = null,
            double? elapsed = null,
            ConversationError? error = null,
            bool canPreview = false)
        {
            return new ConversationEntryRenderModel(
                id,
                role,
                text,
                reasoning,
                stages ?? Array.Empty<ConversationStageTiming>(),
                currentStage,
                elapsed,
                error,
                canPreview);
        }

        private sealed class ViewFixture : IDisposable
        {
            private readonly GameObject root;

            public ViewFixture()
            {
                root = new GameObject("root", typeof(RectTransform));
                ((RectTransform)root.transform).sizeDelta = new Vector2(468, 100);
                var viewport = CreateRect(root.transform, "viewport");
                Content = CreateRect(viewport, "content");
                Composer = CreateRect(root.transform, "composer");
                viewport.anchorMin = Vector2.zero;
                viewport.anchorMax = Vector2.one;
                viewport.offsetMin = Vector2.zero;
                viewport.offsetMax = Vector2.zero;
                Content.anchorMin = new Vector2(0, 1);
                Content.anchorMax = new Vector2(1, 1);
                Content.pivot = new Vector2(0.5f, 1);
                Content.sizeDelta = new Vector2(0, 500);
                Composer.sizeDelta = new Vector2(468, 88);
                Scroll = root.AddComponent<ScrollRect>();
                Scroll.viewport = viewport;
                Scroll.content = Content;
                Scroll.vertical = true;
                Font = ScriptableObject.CreateInstance<TMP_FontAsset>();
                StockFont = ScriptableObject.CreateInstance<TMP_FontAsset>();
                FontMaterial = new Material(Shader.Find("UI/Default"));
                StockFontMaterial = new Material(Shader.Find("UI/Default"));
                Font.material = FontMaterial;
                StockFont.material = StockFontMaterial;
            }

            public ConversationMessageListView? View { get; set; }
            public RectTransform Content { get; }
            public RectTransform Composer { get; }
            public ScrollRect Scroll { get; }
            public TMP_FontAsset Font { get; }
            public TMP_FontAsset StockFont { get; }
            public Material FontMaterial { get; }
            public Material StockFontMaterial { get; }

            public GameObject Find(string name)
            {
                return FindOrNull(name) ?? throw new AssertionException("Missing GameObject: " + name);
            }

            public GameObject? FindOrNull(string name)
            {
                return Content.GetComponentsInChildren<Transform>(true)
                    .Select(item => item.gameObject)
                    .FirstOrDefault(item => item.name == name);
            }

            public TMP_Text Text(string name)
            {
                return Find(name).GetComponent<TMP_Text>();
            }

            public Button Button(string name)
            {
                return Find(name).GetComponent<Button>();
            }

            public void Dispose()
            {
                View?.Dispose();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(Font);
                UnityEngine.Object.DestroyImmediate(StockFont);
                UnityEngine.Object.DestroyImmediate(FontMaterial);
                UnityEngine.Object.DestroyImmediate(StockFontMaterial);
            }

            private static RectTransform CreateRect(Transform parent, string name)
            {
                var gameObject = new GameObject(name, typeof(RectTransform));
                gameObject.transform.SetParent(parent);
                return (RectTransform)gameObject.transform;
            }
        }

        private sealed class TestTmpText : TMP_Text
        {
            protected override void LoadFontAsset()
            {
            }

            protected override void SetSharedMaterial(Material material)
            {
                m_sharedMaterial = material;
            }
        }
    }
}
