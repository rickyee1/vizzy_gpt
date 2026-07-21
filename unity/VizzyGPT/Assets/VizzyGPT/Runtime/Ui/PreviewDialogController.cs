#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using ModApi.Ui;
using TMPro;
using UnityEngine;
using VizzyGPT.Core.Changes;

namespace VizzyGPT.Runtime.Ui
{
    public sealed class PreviewDialogModel
    {
        private PreviewDialogModel(
            string summary,
            IReadOnlyList<string> added,
            IReadOnlyList<string> changed,
            IReadOnlyList<string> removed,
            IReadOnlyList<string> warnings)
        {
            Summary = summary;
            Added = added;
            Changed = changed;
            Removed = removed;
            Warnings = warnings;
        }

        public string Summary { get; }

        public IReadOnlyList<string> Added { get; }

        public IReadOnlyList<string> Changed { get; }

        public IReadOnlyList<string> Removed { get; }

        public IReadOnlyList<string> Warnings { get; }

        public static PreviewDialogModel FromSession(ChangeSession session)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var changes = session.PreviewLines.Take(session.Patch.Operations.Count).ToArray();
            var warnings = session.PreviewLines.Skip(changes.Length).ToArray();
            return new PreviewDialogModel(
                session.Patch.Summary,
                changes.Where(line => line.StartsWith("Added", StringComparison.OrdinalIgnoreCase)).ToArray(),
                changes.Where(line => !line.StartsWith("Added", StringComparison.OrdinalIgnoreCase) &&
                    !line.StartsWith("Removed", StringComparison.OrdinalIgnoreCase)).ToArray(),
                changes.Where(line => line.StartsWith("Removed", StringComparison.OrdinalIgnoreCase)).ToArray(),
                warnings);
        }
    }

    public sealed class PreviewDialogController : MonoBehaviour
    {
        private VizzyGptPanelWorkflow? workflow;
        private Action? close;
        private TMP_Text? summaryText;
        private TMP_Text? addedText;
        private TMP_Text? changedText;
        private TMP_Text? removedText;
        private TMP_Text? warningsText;

        public void Configure(VizzyGptPanelWorkflow value, Action closeAction)
        {
            workflow = value ?? throw new ArgumentNullException(nameof(value));
            close = closeAction ?? throw new ArgumentNullException(nameof(closeAction));
        }

        public void Bind(IXmlLayout layout, PreviewDialogModel model)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (model == null)
            {
                throw new ArgumentNullException(nameof(model));
            }

            summaryText = layout.GetElementById<TMP_Text>("summary-text");
            addedText = layout.GetElementById<TMP_Text>("added-text");
            changedText = layout.GetElementById<TMP_Text>("changed-text");
            removedText = layout.GetElementById<TMP_Text>("removed-text");
            warningsText = layout.GetElementById<TMP_Text>("warnings-text");
            Render(model);
        }

        public async void OnApplyButtonClicked()
        {
            if (workflow != null && await workflow.ApplySessionAsync())
            {
                close?.Invoke();
            }
        }

        public void OnCancelButtonClicked()
        {
            close?.Invoke();
        }

        private void Render(PreviewDialogModel model)
        {
            SetText(summaryText, model.Summary);
            SetText(addedText, model.Added);
            SetText(changedText, model.Changed);
            SetText(removedText, model.Removed);
            SetText(warningsText, model.Warnings);
        }

        private static void SetText(TMP_Text? target, string value)
        {
            if (target != null)
            {
                target.text = SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
            }
        }

        private static void SetText(TMP_Text? target, IReadOnlyList<string> values)
        {
            if (target != null)
            {
                target.text = string.Join("\n", values.Select(value => SecurityElement.Escape(value ?? string.Empty) ?? string.Empty));
            }
        }

        private void OnDestroy()
        {
            close = null;
            workflow = null;
        }
    }
}
