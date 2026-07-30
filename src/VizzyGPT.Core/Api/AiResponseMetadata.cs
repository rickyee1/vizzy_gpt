namespace VizzyGPT.Core.Api
{
    public sealed class AiResponseMetadata
    {
        public static readonly AiResponseMetadata Empty =
            new AiResponseMetadata(null, null, null, false);

        public AiResponseMetadata(
            string? reasoningSummary,
            int? inputTokens,
            int? outputTokens,
            bool wasSchemaRepair)
        {
            ReasoningSummary = string.IsNullOrWhiteSpace(reasoningSummary) ? null : reasoningSummary;
            InputTokens = inputTokens;
            OutputTokens = outputTokens;
            WasSchemaRepair = wasSchemaRepair;
        }

        public string? ReasoningSummary { get; }

        public int? InputTokens { get; }

        public int? OutputTokens { get; }

        public bool WasSchemaRepair { get; }
    }
}
