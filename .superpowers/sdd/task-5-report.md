# Task 5 Phase A Report

## Scope

Phase A defines RED contracts only. No production files, project files, existing tests, or fixtures are changed. The test suite is intentionally not run because the controller owns RED capture.

## Case Matrix

| Area | NUnit cases | Contract coverage |
| --- | ---: | --- |
| OpenAI client and transport | 36 | Responses request/standard output/top-level `output_text`/direct envelope; Chat Completions request and standard choice; narrow Auto fallback; no retry/repair for HTTP or transport failures; one repair only; safe text-only failure; base URI policy; transport request invariants. |
| Context builder | 10 | Small complete editor context; large summaries and ID/path selection; missing/ambiguous/oversized selections; normalized and redacted output; 200-log and 120-sample flight bounds; deterministic metric aggregation and total byte bound. |
| Secret redaction | 13 | Configured key occurrences; case-insensitive bearer headers; spaced/escaped JSON `api_key`; mixed repeats; null/empty configured key; unrelated text; complete placeholder replacement without partial leaks. |
| **Total** | **59** | Deterministic, queue-backed, no-network contracts. |

## Planned Public API

```csharp
namespace VizzyGPT.Core.Api
{
    public enum ApiMode
    {
        Auto,
        Responses,
        ChatCompletions
    }

    public sealed class AiRequest
    {
        public AiRequest(
            ApiMode mode,
            string prompt,
            string context,
            string model,
            Uri baseUri,
            string apiKey,
            TimeSpan timeout);

        public ApiMode Mode { get; }
        public string Prompt { get; }
        public string Context { get; }
        public string Model { get; }
        public Uri BaseUri { get; }
        public string ApiKey { get; }
        public TimeSpan Timeout { get; }
    }

    public sealed class AiResponse
    {
        public AiResponse(
            string message,
            PatchDocument? patch,
            bool canApply,
            IReadOnlyList<string> diagnostics);

        public string Message { get; }
        public PatchDocument? Patch { get; }
        public bool CanApply { get; }
        public IReadOnlyList<string> Diagnostics { get; }
    }

    public interface IAiTransport
    {
        Task<HttpTransportResponse> SendAsync(
            HttpTransportRequest request,
            CancellationToken cancellationToken);
    }

    public sealed class HttpTransportRequest
    {
        public HttpTransportRequest(
            string method,
            Uri uri,
            IReadOnlyDictionary<string, string> headers,
            byte[] body,
            TimeSpan timeout);

        public string Method { get; }
        public Uri Uri { get; }
        public IReadOnlyDictionary<string, string> Headers { get; }
        public byte[] Body { get; }
        public TimeSpan Timeout { get; }
    }

    public sealed class HttpTransportResponse
    {
        public HttpTransportResponse(int statusCode, byte[] body);
        public int StatusCode { get; }
        public byte[] Body { get; }
    }

    public sealed class OpenAiApiException : Exception
    {
        public int StatusCode { get; }
    }

    public sealed class OpenAiClient
    {
        public OpenAiClient(IAiTransport transport);
        public Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken);
    }

    public sealed class FlightLogEntry
    {
        public FlightLogEntry(DateTime timestampUtc, string message);
        public DateTime TimestampUtc { get; }
        public string Message { get; }
    }

    public sealed class TelemetrySample
    {
        public TelemetrySample(DateTime timestampUtc, IReadOnlyDictionary<string, double> metrics);
        public DateTime TimestampUtc { get; }
        public IReadOnlyDictionary<string, double> Metrics { get; }
    }

    public sealed class ContextBuilder
    {
        public ContextBuilder(string? configuredApiKey = null);

        public string BuildEditorContext(
            VizzyProgramDocument document,
            string declarations,
            NodeSelector? selection);

        public string BuildFlightContext(
            IReadOnlyList<FlightLogEntry> logs,
            IReadOnlyList<TelemetrySample> telemetry);
    }
}

namespace VizzyGPT.Core.Security
{
    public static class SecretRedactor
    {
        public static string Redact(string text, string? configuredApiKey = null);
    }
}
```

## Behavioral Decisions

- `AiRequest.BaseUri` is normalized by removing all trailing `/` characters from its path. Endpoint paths are then appended with exactly one separator while preserving a non-root base path.
- HTTPS accepts any host. HTTP accepts only exact `localhost`, `127.0.0.1`, and `[::1]` loopback authorities. Relative URIs, credentials, fragments, other schemes, and other HTTP hosts are rejected before transport use.
- `HttpTransportRequest.Headers` is a defensive ordinal copy. `Body` is UTF-8 JSON bytes, `Uri` is absolute, and `Timeout` must be positive.
- Auto mode starts at Responses. It makes one Chat Completions fallback only when status is `404` or `405` and the structured or textual error identifies the Responses endpoint as missing, unsupported, unavailable, or not found. An application-level missing resource does not qualify.
- HTTP failures produce `OpenAiApiException`; cancellation and timeout exceptions from the transport propagate unchanged. There are no retries.
- A successful model envelope is validated by `PatchDocument.Deserialize`. The first invalid envelope or patch causes one same-endpoint repair request containing both the invalid output and validation error. A second invalid output returns a nonempty, text-only `AiResponse` with no patch, `CanApply == false`, and bounded single-line redacted diagnostics.
- Compatible Responses normalization accepts the official `output[].content[]` `output_text.text` shape, a top-level `output_text` string used by SDK-compatible proxies, and a direct envelope object used by simple OpenAI-compatible local servers. Chat Completions accepts `choices[0].message.content`.
- If canonical XML is at most 64 KiB UTF-8, editor context includes it completely with declarations. Otherwise, large-mode output is capped at 64 KiB and contains declarations, root event/custom-node summaries, and at most one selected subtree. Duplicate ID selection is ambiguous, absent ID/path selection is missing, and neither chooses an arbitrary subtree. Oversized sections are deterministically truncated with `[TRUNCATED]`.
- Flight input is sorted by UTC timestamp. The latest 200 logs are emitted chronologically. The latest 120 telemetry samples are aggregated by ordinal metric name with invariant-culture `min`, `max`, and latest values. Final output is capped at 64 KiB UTF-8.
- Context and diagnostic output normalizes CRLF/CR to LF and applies `SecretRedactor` before exposure.

## Official Endpoint Assumptions

Checked against official OpenAI API documentation on 2026-07-21:

- Responses uses `POST /v1/responses`, accepts `model` and string `input`, and returns assistant text in message output content items whose type is `output_text`.
- Responses Structured Outputs uses `text.format` with `type: "json_schema"`, `name`, `strict: true`, and `schema`.
- Chat Completions uses `POST /v1/chat/completions`, accepts `model` and `messages`, returns text in `choices[0].message.content`, and enables Structured Outputs with `response_format: { type: "json_schema", json_schema: { ... } }`.
- The top-level Responses `output_text` and direct-envelope forms are compatibility extensions for proxies/local servers, not asserted as raw REST response forms from OpenAI.

References:

- https://developers.openai.com/api/reference/resources/responses/methods/create
- https://developers.openai.com/api/reference/resources/chat/subresources/completions/methods/create
- https://developers.openai.com/api/docs/guides/structured-outputs

## Phase A Checks

- Tests are intentionally not run; the controller captures RED.
- Queue-backed `FakeTransport` has no network path and throws if a test sends more requests than queued.
- Static self-review and `git diff --check` are required before commit.

## Concerns

- The compatibility fallback body vocabulary is intentionally semantic rather than tied to one provider-specific error code. Phase B should keep the status-and-body conjunction narrow and covered by these tests.
- The 64 KiB contract applies to final UTF-8 context, so truncation must operate on UTF-8 boundaries and must not split surrogate pairs.
