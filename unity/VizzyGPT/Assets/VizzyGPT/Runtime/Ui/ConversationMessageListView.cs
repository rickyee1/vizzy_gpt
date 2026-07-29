#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VizzyGPT.Core.Conversations;

namespace VizzyGPT.Runtime.Ui
{
    public sealed class ConversationMessageListView : IDisposable
    {
        private const float BottomThreshold = 0.05f;

        private readonly RectTransform content;
        private readonly ScrollRect scroll;
        private readonly RectTransform composer;
        private readonly TMP_FontAsset? cjkFont;
        private readonly Action showPreview;
        private readonly Func<GameObject, TMP_Text> createTextComponent;
        private readonly Dictionary<string, MessageRow> rows = new Dictionary<string, MessageRow>();
        private readonly HashSet<string> expandedReasoning = new HashSet<string>();
        private readonly HashSet<string> expandedErrors = new HashSet<string>();
        private bool disposed;

        public ConversationMessageListView(
            RectTransform content,
            ScrollRect scroll,
            RectTransform composer,
            TMP_FontAsset? cjkFont,
            Action showPreview,
            Func<GameObject, TMP_Text>? createTextComponent = null)
        {
            this.content = content ?? throw new ArgumentNullException(nameof(content));
            this.scroll = scroll ?? throw new ArgumentNullException(nameof(scroll));
            this.composer = composer ?? throw new ArgumentNullException(nameof(composer));
            this.cjkFont = cjkFont;
            this.showPreview = showPreview ?? throw new ArgumentNullException(nameof(showPreview));
            this.createTextComponent = createTextComponent ??
                (gameObject => gameObject.AddComponent<TextMeshProUGUI>());
            ConfigureContentLayout();
        }

        public void Render(IReadOnlyList<ConversationEntryRenderModel> entries)
        {
            ThrowIfDisposed();
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            var preserveScrollPosition = scroll.verticalNormalizedPosition;
            var shouldScrollToBottom = preserveScrollPosition <= BottomThreshold;
            var activeIds = new HashSet<string>(entries.Select(entry => entry.Id));
            foreach (var staleId in rows.Keys.Where(id => !activeIds.Contains(id)).ToArray())
            {
                DestroyRow(rows[staleId].Root);
                rows.Remove(staleId);
                expandedReasoning.Remove(staleId);
                expandedErrors.Remove(staleId);
            }

            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (!rows.TryGetValue(entry.Id, out var row))
                {
                    row = CreateRow(entry);
                    rows.Add(entry.Id, row);
                }

                UpdateRow(row, entry);
                row.Root.transform.SetSiblingIndex(index);
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();
            scroll.verticalNormalizedPosition = shouldScrollToBottom ? 0f : preserveScrollPosition;
        }

        public void RefreshDynamicTextFonts()
        {
            ThrowIfDisposed();
            CjkTextFontApplicator.ApplyToText(
                cjkFont,
                rows.Values.SelectMany(row => row.DynamicTexts).ToArray());
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            foreach (var row in rows.Values)
            {
                DestroyRow(row.Root);
            }

            rows.Clear();
            expandedReasoning.Clear();
            expandedErrors.Clear();
        }

        private void ConfigureContentLayout()
        {
            var layout = content.GetComponent<VerticalLayoutGroup>() ??
                content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 12f;

            var fitter = content.GetComponent<ContentSizeFitter>() ??
                content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }

        private MessageRow CreateRow(ConversationEntryRenderModel entry)
        {
            var root = new GameObject("message-" + entry.Id, typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter), typeof(LayoutElement));
            root.transform.SetParent(content, false);
            var layout = root.GetComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 8, 8);
            layout.spacing = 5f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var fitter = root.GetComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            root.GetComponent<LayoutElement>().minWidth = 0f;

            if (entry.Role == ConversationRole.User)
            {
                var background = root.AddComponent<Image>();
                background.color = new Color(0.16f, 0.18f, 0.21f, 0.9f);
                background.raycastTarget = false;
            }

            var role = CreateText(root.transform, root.name + "-role", 13f, FontStyles.Bold, new Color(0.78f, 0.82f, 0.88f, 1f));
            var body = CreateText(root.transform, root.name + "-body", 16f, FontStyles.Normal, Color.white);
            var meta = CreateText(root.transform, root.name + "-meta", 12f, FontStyles.Normal, new Color(0.62f, 0.67f, 0.73f, 1f));
            var reasoningButton = CreateButton(root.transform, root.name + "-reasoning-button", "Reasoning");
            var reasoningDetail = CreateText(root.transform, root.name + "-reasoning-detail", 13f, FontStyles.Normal, new Color(0.78f, 0.82f, 0.88f, 1f));
            var errorButton = CreateButton(root.transform, root.name + "-error-button", "Technical details");
            var errorDetail = CreateText(root.transform, root.name + "-error-detail", 13f, FontStyles.Italic, new Color(1f, 0.69f, 0.62f, 1f));
            var previewButton = CreateButton(root.transform, root.name + "-preview-button", "Preview changes");

            var row = new MessageRow(
                root,
                role,
                body,
                meta,
                reasoningButton,
                reasoningDetail,
                errorButton,
                errorDetail,
                previewButton);
            reasoningButton.onClick.AddListener(() => ToggleReasoning(entry.Id, row));
            errorButton.onClick.AddListener(() => ToggleError(entry.Id, row));
            previewButton.onClick.AddListener(() => showPreview());
            return row;
        }

        private void UpdateRow(MessageRow row, ConversationEntryRenderModel entry)
        {
            row.Role.text = FormatRole(entry.Role);
            row.Body.text = entry.Text;
            row.Meta.text = FormatMetadata(entry);
            row.Meta.gameObject.SetActive(row.Meta.text.Length > 0);

            var hasReasoning = !string.IsNullOrWhiteSpace(entry.ReasoningSummary) || entry.Stages.Count > 0;
            row.ReasoningButton.gameObject.SetActive(hasReasoning);
            row.ReasoningButtonLabel.text = FormatReasoningLabel(entry);
            row.ReasoningDetail.text = FormatReasoningDetail(entry);
            row.ReasoningDetail.gameObject.SetActive(hasReasoning && expandedReasoning.Contains(entry.Id));

            var hasError = entry.Error != null;
            row.ErrorButton.gameObject.SetActive(hasError);
            row.ErrorDetail.text = FormatErrorDetail(entry.Error);
            row.ErrorDetail.gameObject.SetActive(hasError && expandedErrors.Contains(entry.Id));
            row.PreviewButton.gameObject.SetActive(entry.CanPreview);
            RefreshRowFonts(row);
        }

        private void ToggleReasoning(string id, MessageRow row)
        {
            if (!expandedReasoning.Add(id))
            {
                expandedReasoning.Remove(id);
            }

            row.ReasoningDetail.gameObject.SetActive(expandedReasoning.Contains(id));
            RebuildAfterDisclosure();
        }

        private void ToggleError(string id, MessageRow row)
        {
            if (!expandedErrors.Add(id))
            {
                expandedErrors.Remove(id);
            }

            row.ErrorDetail.gameObject.SetActive(expandedErrors.Contains(id));
            RebuildAfterDisclosure();
        }

        private void RebuildAfterDisclosure()
        {
            var composerSize = composer.sizeDelta;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            composer.sizeDelta = composerSize;
        }

        private TMP_Text CreateText(
            Transform parent,
            string name,
            float fontSize,
            FontStyles style,
            Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(LayoutElement));
            gameObject.transform.SetParent(parent, false);
            var text = createTextComponent(gameObject);
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.color = color;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.raycastTarget = false;
            gameObject.GetComponent<LayoutElement>().minHeight = fontSize + 6f;
            CjkTextFontApplicator.ApplyToText(cjkFont, text);
            return text;
        }

        private Button CreateButton(Transform parent, string name, string label)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button), typeof(LayoutElement));
            gameObject.transform.SetParent(parent, false);
            var image = gameObject.GetComponent<Image>();
            image.color = new Color(0.22f, 0.25f, 0.29f, 1f);
            var layout = gameObject.GetComponent<LayoutElement>();
            layout.preferredHeight = 30f;
            layout.flexibleWidth = 0f;
            var text = CreateText(gameObject.transform, name + "-label", 13f, FontStyles.Normal, Color.white);
            var rect = (RectTransform)text.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(8f, 3f);
            rect.offsetMax = new Vector2(-8f, -3f);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.text = label;
            return gameObject.GetComponent<Button>();
        }

        private void RefreshRowFonts(MessageRow row)
        {
            CjkTextFontApplicator.ApplyToText(cjkFont, row.DynamicTexts);
        }

        private static string FormatRole(ConversationRole role)
        {
            switch (role)
            {
                case ConversationRole.User:
                    return "You";
                case ConversationRole.Assistant:
                    return "Vizzy GPT";
                default:
                    return "System";
            }
        }

        private static string FormatMetadata(ConversationEntryRenderModel entry)
        {
            if (entry.CurrentStage.HasValue)
            {
                return SplitPascalCase(entry.CurrentStage.Value.ToString()) +
                    (entry.ElapsedSeconds.HasValue ? " - " + FormatSeconds(entry.ElapsedSeconds.Value) : string.Empty);
            }

            if (entry.ElapsedSeconds.HasValue)
            {
                return FormatSeconds(entry.ElapsedSeconds.Value);
            }

            if (entry.Stages.Count > 0)
            {
                return "Completed in " + FormatSeconds(entry.Stages.Sum(stage => stage.ElapsedSeconds));
            }

            return string.Empty;
        }

        private static string FormatReasoningLabel(ConversationEntryRenderModel entry)
        {
            var total = entry.Stages.Sum(stage => stage.ElapsedSeconds);
            return total > 0 ? "Thought for " + FormatSeconds(total) : "Reasoning";
        }

        private static string FormatReasoningDetail(ConversationEntryRenderModel entry)
        {
            var builder = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(entry.ReasoningSummary))
            {
                builder.Append(entry.ReasoningSummary!.Trim());
            }

            foreach (var stage in entry.Stages)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(SplitPascalCase(stage.Stage));
                builder.Append(": ");
                builder.Append(FormatSeconds(stage.ElapsedSeconds));
            }

            return builder.ToString();
        }

        private static string FormatErrorDetail(ConversationError? error)
        {
            if (error == null)
            {
                return string.Empty;
            }

            var path = string.IsNullOrWhiteSpace(error.Path) ? string.Empty : "\nPath: " + error.Path;
            return error.Code + " at " + error.Stage + path + "\n" + error.TechnicalDetails;
        }

        private static string FormatSeconds(double seconds)
        {
            return seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s";
        }

        private static string SplitPascalCase(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length + 4);
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (index > 0 && char.IsUpper(character) && !char.IsUpper(value[index - 1]))
                {
                    builder.Append(' ');
                }

                builder.Append(character);
            }

            return builder.ToString();
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(ConversationMessageListView));
            }
        }

        private static void DestroyRow(GameObject row)
        {
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(row);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(row);
            }
        }

        private sealed class MessageRow
        {
            public MessageRow(
                GameObject root,
                TMP_Text role,
                TMP_Text body,
                TMP_Text meta,
                Button reasoningButton,
                TMP_Text reasoningDetail,
                Button errorButton,
                TMP_Text errorDetail,
                Button previewButton)
            {
                Root = root;
                Role = role;
                Body = body;
                Meta = meta;
                ReasoningButton = reasoningButton;
                ReasoningButtonLabel = reasoningButton.GetComponentInChildren<TMP_Text>(true);
                ReasoningDetail = reasoningDetail;
                ErrorButton = errorButton;
                ErrorButtonLabel = errorButton.GetComponentInChildren<TMP_Text>(true);
                ErrorDetail = errorDetail;
                PreviewButton = previewButton;
                PreviewButtonLabel = previewButton.GetComponentInChildren<TMP_Text>(true);
                DynamicTexts = new[]
                {
                    Role,
                    Body,
                    Meta,
                    ReasoningButtonLabel,
                    ReasoningDetail,
                    ErrorButtonLabel,
                    ErrorDetail,
                    PreviewButtonLabel
                };
            }

            public GameObject Root { get; }
            public TMP_Text Role { get; }
            public TMP_Text Body { get; }
            public TMP_Text Meta { get; }
            public Button ReasoningButton { get; }
            public TMP_Text ReasoningButtonLabel { get; }
            public TMP_Text ReasoningDetail { get; }
            public Button ErrorButton { get; }
            public TMP_Text ErrorButtonLabel { get; }
            public TMP_Text ErrorDetail { get; }
            public Button PreviewButton { get; }
            public TMP_Text PreviewButtonLabel { get; }
            public TMP_Text[] DynamicTexts { get; }
        }
    }
}
