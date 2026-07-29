using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace VizzyGPT.Core.Conversations
{
    public enum ConversationRole
    {
        User,
        Assistant,
        System
    }

    public enum ConversationMessageKind
    {
        Message,
        Progress,
        Error,
        Cancelled
    }

    public enum ConversationMode
    {
        Ask,
        Modify
    }

    public sealed class ConversationStageTiming
    {
        [JsonConstructor]
        public ConversationStageTiming(string stage, double elapsedSeconds)
        {
            Stage = RequireText(stage, nameof(stage));
            ElapsedSeconds = RequireDuration(elapsedSeconds, nameof(elapsedSeconds));
        }

        public string Stage { get; }

        public double ElapsedSeconds { get; }

        internal static double RequireDuration(double value, string parameterName)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Duration must be finite and non-negative.");
            }

            return value;
        }

        internal static string RequireText(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value must be non-whitespace.", parameterName);
            }

            return value;
        }
    }

    public sealed class ConversationError
    {
        private const int MaximumFieldLength = 2048;

        [JsonConstructor]
        public ConversationError(
            string code,
            string stage,
            string summary,
            string technicalDetails,
            string? path)
        {
            Code = RequireBoundedText(code, nameof(code));
            Stage = RequireBoundedText(stage, nameof(stage));
            Summary = RequireBoundedText(summary, nameof(summary));
            TechnicalDetails = RequireBoundedText(technicalDetails, nameof(technicalDetails));
            Path = path == null ? null : RequireBoundedText(path, nameof(path));
        }

        public string Code { get; }

        public string Stage { get; }

        public string Summary { get; }

        public string TechnicalDetails { get; }

        public string? Path { get; }

        private static string RequireBoundedText(string value, string parameterName)
        {
            var validated = ConversationStageTiming.RequireText(value, parameterName);
            if (validated.Length > MaximumFieldLength)
            {
                throw new ArgumentException(
                    "Sanitized error fields must not exceed 2,048 characters.",
                    parameterName);
            }

            return validated;
        }
    }

    public sealed class ConversationMessage
    {
        [JsonConstructor]
        public ConversationMessage(
            string id,
            ConversationRole role,
            ConversationMessageKind kind,
            ConversationMode mode,
            string text,
            string? reasoningSummary,
            IReadOnlyList<ConversationStageTiming> stages,
            double? elapsedSeconds,
            ConversationError? error,
            DateTime createdUtc)
        {
            Id = ConversationStageTiming.RequireText(id, nameof(id));
            Role = RequireDefined(role, nameof(role));
            Kind = RequireDefined(kind, nameof(kind));
            Mode = RequireDefined(mode, nameof(mode));
            Text = text ?? throw new ArgumentNullException(nameof(text));
            ReasoningSummary = reasoningSummary;
            Stages = (stages ?? throw new ArgumentNullException(nameof(stages))).ToArray();
            if (Stages.Any(stage => stage == null))
            {
                throw new ArgumentException("Stage timings must not contain null entries.", nameof(stages));
            }

            ElapsedSeconds = elapsedSeconds.HasValue
                ? ConversationStageTiming.RequireDuration(elapsedSeconds.Value, nameof(elapsedSeconds))
                : null;
            Error = error;
            if (createdUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Conversation timestamp must be UTC.", nameof(createdUtc));
            }

            CreatedUtc = createdUtc;
        }

        public string Id { get; }

        public ConversationRole Role { get; }

        public ConversationMessageKind Kind { get; }

        public ConversationMode Mode { get; }

        public string Text { get; }

        public string? ReasoningSummary { get; }

        public IReadOnlyList<ConversationStageTiming> Stages { get; }

        public double? ElapsedSeconds { get; }

        public ConversationError? Error { get; }

        public DateTime CreatedUtc { get; }

        private static TEnum RequireDefined<TEnum>(TEnum value, string parameterName)
            where TEnum : struct, Enum
        {
            if (!Enum.IsDefined(typeof(TEnum), value))
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }

            return value;
        }
    }
}
