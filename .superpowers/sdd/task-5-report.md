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

# Task 5 Phase B Report

## Production Scope

Implemented only the owned API, context, and security files. Tests, project files, and prior production source were not modified. The implementation is transport-neutral and makes no real network requests.

## Commands And Results

1. Initial RED:

   ```powershell
   dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --filter "FullyQualifiedName~OpenAiClientTests|FullyQualifiedName~SecretRedactorTests" --no-restore
   ```

   Result: build failed with the expected 13 compile errors, all for missing `VizzyGPT.Core.Api` / `VizzyGPT.Core.Security` Task 5 types.

2. First post-implementation focused attempt used the same command. Both assemblies compiled, but VSTest aborted before executing tests because the sandboxed test host could not query its parent process (`Win32Exception (5)`). The repository wrapper's `CODEX_SHELL` path is required in this environment.

3. First executable full run:

   ```powershell
   & powershell -ExecutionPolicy Bypass -File 'tools\Test-Core.ps1'; $code=$LASTEXITCODE; git status --short; exit $code
   ```

   Result: build 0 warnings / 0 errors; 404 passed, 4 failed, 408 total. Two production issues were identified and fixed: bracketed IPv6 loopback host representation and large-mode detection for preserved source whitespace. The other two failures were the schema-test issue documented below.

4. Full rerun after those fixes, using the same wrapper command: build 0 warnings / 0 errors; 406 passed, 2 failed, 408 total.

5. Focused command after the wrapper prepared the parent-process-safe Release test host:

   ```powershell
   dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OpenAiClientTests|FullyQualifiedName~SecretRedactorTests"
   ```

   Result: 47 passed, 2 failed, 49 total. The brief's exact filter does not include the separately named `ContextBuilderTests` fixture.

6. Complete Task 5 selection:

   ```powershell
   dotnet test tests/VizzyGPT.Core.Tests/VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OpenAiClientTests|FullyQualifiedName~ContextBuilderTests|FullyQualifiedName~SecretRedactorTests"
   ```

   Result: 57 passed, 2 failed, 59 total. Every Task 5 case passes except the Responses and Chat strict-schema tests described below.

## Self-Review

- Automatic request counts are bounded: one normal request; one request for non-fallback HTTP/transport failures; at most two for endpoint fallback; at most two for same-endpoint schema repair; and at most three when an Auto fallback response itself needs the one repair. No transport retry loop exists.
- Cancellation and timeout exceptions are not caught. HTTP failures are redacted before constructing `OpenAiApiException`.
- Request bodies never contain the API key; it appears only in the required `Authorization` header. All exposed API/model diagnostics pass through exact-key, bearer, and JSON `api_key` redaction and single-line bounding.
- Request/response byte arrays and header/metric/diagnostic collections are defensively copied with ordinal dictionaries where required.
- Context truncation is UTF-8 bounded, surrogate-safe, deterministic, and performed after line normalization and redaction.
- No real transport, network endpoint, or live API key was used.

## Blocking Test Concern

The two remaining tests fail inside `AssertAllObjectSchemasAreStrict` at `OpenAiClientTests.cs:404`, before evaluating the produced schema. The helper traverses every descendant `JObject` and unconditionally casts `candidate["type"]` to `string`. Each operation variant is also required later by the same test to expose `item["properties"]["type"]["const"]`; therefore its `properties` object necessarily has a child named `type` whose value is a `JObject`. Casting that object to `string` throws `ArgumentException: Can not convert Object to String.`

This is contradictory for any JSON schema represented by a parsed Newtonsoft `JObject`: satisfying the later discriminator assertion necessarily triggers the earlier cast. Production cannot make both assertions pass, and strict ownership forbids correcting the test helper to guard for `JTokenType.String`. The implementation retains the required ten strict operation variants rather than weakening the schema or adding test-specific production behavior.

## Controller Reproduction and Harness Correction

The controller independently reproduced exactly two failures at `OpenAiClientTests.cs:404`, matching the blocking concern above. Both failures occurred before schema assertions because traversal treated every descendant `JObject` with a child named `type` as if that child were a JSON string. In particular, an operation variant's `properties` object has a `type` child whose value is the discriminator schema object containing `const`.

The test helper predicate now classifies a candidate as an object schema only when its own `type` token has `JTokenType.String` and the string value is ordinal-equal to `object`. No production code or schema assertion changed.

### Harness Correction Verification

| Command | Result |
| --- | --- |
| `powershell -ExecutionPolicy Bypass -File tools\Test-Core.ps1` | Build 0 warnings / 0 errors; 408 passed, 0 failed, 0 skipped. |
| `dotnet test tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj --configuration Release --no-build --filter "FullyQualifiedName~OpenAiClientTests\|FullyQualifiedName~ContextBuilderTests\|FullyQualifiedName~SecretRedactorTests"` | 59 passed, 0 failed, 0 skipped. |

The initial direct focused invocation built successfully but the Windows testhost parent-process query was denied before case execution. Running the repository wrapper applied its existing workaround; the subsequent focused no-build command then completed normally.

## Task 5 Review Regression Phase A

This follow-up is test-only. Production files remain untouched, and tests are intentionally not run because the controller owns RED capture.

### Official Structured Outputs Constraint

The contract is corrected to match the official Structured Outputs subset documented at https://developers.openai.com/api/docs/guides/structured-outputs:

- Nested unions use `anyOf`; `oneOf` is absent from the emitted schema.
- Every object sets `additionalProperties: false`, exposes `properties`, and lists every property in `required`.
- Optional domain fields are required on the wire and represented as nullable `anyOf` members. `addVariable.value` is the current optional field and normalizes JSON `null` to an omitted/null `PatchOperation.Value`.
- Node attributes use a wire array of strict `{ name, value }` objects so duplicate names can be detected before normalization to the ordinal `NodeSpec.Attributes` dictionary.

### Review Matrix

| Area | New NUnit cases | Regression contract |
| --- | ---: | --- |
| Schema and wire/domain normalization | 3 | Both existing endpoint schema assertions now reject every `oneOf`, require operation/selector `anyOf`, validate all strict object/union members and exact fields for ten operations, require nullable `addVariable.value`, and require recursive NodeSpec attribute arrays. New cases cover null normalization, attributed recursive nodes that apply, and duplicate attribute rejection through one repair. |
| Protocol versus repair | 9 | Malformed Responses JSON, missing output, wrong item role/type, malformed Chat wrappers, official refusals, extracted invalid text for both endpoints, and Auto fallback followed by malformed Chat. Protocol failures/refusals do not consume repair; extracted invalid model text consumes exactly one. |
| Fallback classification | 5 | Terse structured unsupported codes, phrase collision, clearly unavailable textual Responses endpoint, and unrelated textual not-found wording. |
| Transport and URI safety | 5 | Arbitrary transport exception redaction/replacement plus rejection of `127.1`, octal, hexadecimal, and single-integer IPv4 spellings. Existing exact HTTP loopback acceptance remains in place. |
| Context safety and retention | 5 | Canonical-size mode decision, section-preserving huge declarations, HTML/XML entity redaction, newest-log retention under truncation, and deterministic finite-only telemetry summaries. |
| Secret redaction | 2 | Escaped nested JSON and HTML/XML `&quot;api_key&quot;` values. |
| **Total added** | **29** | Total Task 5 coverage becomes 88 cases: 58 client/transport/URI, 15 context, and 15 redaction. |

### Phase A Static Checks

- Queue-backed transport remains deterministic and has no network path.
- No production, project, fixture, or unrelated test file is modified.
- Controller RED execution is pending by instruction; only diff and ownership checks are performed here.

### Selector Helper Compile Correction

The controller build found two nullable-key compiler errors at `OpenAiClientTests.cs:774-775`: Newtonsoft's `Values<string>()` annotates the selected value as nullable, so `ToDictionary` inferred `string?` despite the helper's preceding selector-shape assumptions. The key extraction now uses an explicit null-forgiving assertion after `Single()`. Test behavior and production remain unchanged.

Compile-only verification with `dotnet build tests\VizzyGPT.Core.Tests\VizzyGPT.Core.Tests.csproj --configuration Release --no-restore` succeeded with 0 warnings and 0 errors. No tests were run.
