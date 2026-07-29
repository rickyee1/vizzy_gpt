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
                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "\u4F60\u597D"),
                    Entry("assistant-1", ConversationRole.Assistant, "\u5DF2\u5B8C\u6210", reasoning: "\u601D\u8003"),
                    Entry("user-2", ConversationRole.User, "\u7EE7\u7EED"),
                    Entry("assistant-2", ConversationRole.Assistant, "\u5B8C\u6210")
                });
                Assert.That(fixture.Content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
                fixture.Scroll.verticalNormalizedPosition = 0.65f;
                fixture.Scroll.Rebuild(CanvasUpdate.PostLayout);
                var manualPosition = fixture.Scroll.verticalNormalizedPosition;
                Assert.That(manualPosition, Is.GreaterThan(0.05f));
                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "\u4F60\u597D"),
                    Entry("assistant-1", ConversationRole.Assistant, "\u5DF2\u5B8C\u6210", reasoning: "\u601D\u8003"),
                    Entry("user-2", ConversationRole.User, "\u7EE7\u7EED"),
                    Entry("assistant-2", ConversationRole.Assistant, "\u5B8C\u6210"),
                    Entry("user-3", ConversationRole.User, "\u66F4\u591A")
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
        public void Render_follows_bottom_when_non_scrollable_content_becomes_scrollable()
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

                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "hello")
                });

                Assert.That(fixture.Content.rect.height, Is.LessThanOrEqualTo(fixture.Scroll.viewport.rect.height));
                fixture.Content.anchoredPosition = new Vector2(0f, -100f);
                fixture.Scroll.Rebuild(CanvasUpdate.PostLayout);
                Assert.That(
                    fixture.Scroll.verticalNormalizedPosition,
                    Is.GreaterThan(0.95f),
                    "The test must reproduce Unity's ambiguous top value for non-scrollable content.");

                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "hello"),
                    Entry("assistant-1", ConversationRole.Assistant, "done"),
                    Entry("user-2", ConversationRole.User, "more"),
                    Entry("assistant-2", ConversationRole.Assistant, "more"),
                    Entry("user-3", ConversationRole.User, "more"),
                    Entry("assistant-3", ConversationRole.Assistant, "done")
                });

                Assert.That(fixture.Content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.Zero);
            }
        }

        [Test]
        public void Render_resets_follow_after_manual_scroll_content_shrinks_then_regrows()
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
                var overflowingEntries = new[]
                {
                    Entry("user-1", ConversationRole.User, "one"),
                    Entry("assistant-1", ConversationRole.Assistant, "two"),
                    Entry("user-2", ConversationRole.User, "three"),
                    Entry("assistant-2", ConversationRole.Assistant, "four"),
                    Entry("user-3", ConversationRole.User, "five"),
                    Entry("assistant-3", ConversationRole.Assistant, "six")
                };

                fixture.View.Render(overflowingEntries);
                Assert.That(fixture.Content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
                fixture.Scroll.verticalNormalizedPosition = 0.65f;
                fixture.Scroll.Rebuild(CanvasUpdate.PostLayout);
                var manualPosition = fixture.Scroll.verticalNormalizedPosition;

                fixture.View.Render(overflowingEntries.Concat(new[]
                {
                    Entry("user-4", ConversationRole.User, "seven")
                }).ToArray());

                Assert.That(fixture.Content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.EqualTo(manualPosition).Within(0.001f));

                fixture.View.Render(new[]
                {
                    Entry("user-1", ConversationRole.User, "one")
                });

                Assert.That(fixture.Content.rect.height, Is.LessThanOrEqualTo(fixture.Scroll.viewport.rect.height));
                fixture.Content.anchoredPosition = new Vector2(0f, -100f);
                fixture.Scroll.Rebuild(CanvasUpdate.PostLayout);
                Assert.That(
                    fixture.Scroll.verticalNormalizedPosition,
                    Is.GreaterThan(0.95f),
                    "The regrowth must start from Unity's ambiguous non-scrollable top value.");

                fixture.View.Render(overflowingEntries);

                Assert.That(fixture.Content.rect.height, Is.GreaterThan(fixture.Scroll.viewport.rect.height));
                Assert.That(fixture.Scroll.verticalNormalizedPosition, Is.Zero);
            }
        }

        [Test]
        public void Disclosure_and_preview_labels_remain_single_line_inside_narrow_buttons()
        {
            using (var fixture = new ViewFixture())
            {
                fixture.SetWidth(120f);
                fixture.View = new ConversationMessageListView(
                    fixture.Content,
                    fixture.Scroll,
                    fixture.Composer,
                    fixture.Font,
                    () => { },
                    gameObject => gameObject.AddComponent<TestTmpText>());

                fixture.View.Render(new[]
                {
                    Entry(
                        "assistant-1",
                        ConversationRole.Assistant,
                        "done",
                        reasoning: "reasoning",
                        stages: new[] { new ConversationStageTiming("WaitingForModel", 12345.7) },
                        error: new ConversationError(
                            "invalid_patch",
                            "ValidatingPatch",
                            "message",
                            "technical details",
                            "/Program"),
                        canPreview: true)
                });

                foreach (var buttonName in new[]
                {
                    "message-assistant-1-reasoning-button",
                    "message-assistant-1-error-button",
                    "message-assistant-1-preview-button"
                })
                {
                    var button = fixture.Find(buttonName);
                    var label = button.GetComponentInChildren<TMP_Text>(true);
                    var buttonLayout = button.GetComponent<LayoutElement>();
                    var labelLayout = label.GetComponent<LayoutElement>();

                    Assert.That(label.enableWordWrapping, Is.False, buttonName);
                    Assert.That(label.overflowMode, Is.EqualTo(TextOverflowModes.Ellipsis), buttonName);
                    Assert.That(
                        buttonLayout.preferredHeight,
                        Is.GreaterThanOrEqualTo(labelLayout.minHeight + 6f),
                        buttonName);
                    Assert.That(((RectTransform)label.transform).offsetMin.y, Is.GreaterThanOrEqualTo(0f), buttonName);
                    Assert.That(((RectTransform)label.transform).offsetMax.y, Is.LessThanOrEqualTo(0f), buttonName);
                }
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

            public void SetWidth(float width)
            {
                ((RectTransform)root.transform).SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                Composer.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
            }

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
