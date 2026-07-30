#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using VizzyGPT.Core.Conversations;

namespace VizzyGPT.Runtime.Ui
{
    public enum RequestStage
    {
        ReadingProgram,
        BuildingContext,
        WaitingForModel,
        ParsingPatch,
        ValidatingPatch,
        RepairingPatch,
        PreparingPreview,
        SavingPending
    }

    public sealed class ConversationEntryRenderModel
    {
        public ConversationEntryRenderModel(
            string id,
            ConversationRole role,
            string text,
            string? reasoningSummary,
            IReadOnlyList<ConversationStageTiming> stages,
            RequestStage? currentStage,
            double? elapsedSeconds,
            ConversationError? error,
            bool canPreview)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Role = role;
            Text = text ?? throw new ArgumentNullException(nameof(text));
            ReasoningSummary = reasoningSummary;
            Stages = (stages ?? throw new ArgumentNullException(nameof(stages))).ToArray();
            CurrentStage = currentStage;
            ElapsedSeconds = elapsedSeconds;
            Error = error;
            CanPreview = canPreview;
        }

        public string Id { get; }
        public ConversationRole Role { get; }
        public string Text { get; }
        public string? ReasoningSummary { get; }
        public IReadOnlyList<ConversationStageTiming> Stages { get; }
        public RequestStage? CurrentStage { get; }
        public double? ElapsedSeconds { get; }
        public ConversationError? Error { get; }
        public bool CanPreview { get; }
    }
}
