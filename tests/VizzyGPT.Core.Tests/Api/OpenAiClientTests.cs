using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Tests.Api
{
    public sealed class OpenAiClientTests
    {
        private const string ApiKey = "sk-task5-secret";
        private const string BaseHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

        [Test]
        public async Task Responses_posts_strict_schema_request_and_normalizes_standard_output()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Added yaw"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            var request = transport.Requests[0];
            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Uri, Is.EqualTo(new Uri("https://api.example.test/openai/v1/responses")));
            Assert.That(request.Headers["Authorization"], Is.EqualTo("Bearer " + ApiKey));
            Assert.That(request.Headers["Content-Type"], Is.EqualTo("application/json"));
            Assert.That(request.Timeout, Is.EqualTo(TimeSpan.FromSeconds(17)));

            var payload = ParseBody(request);
            Assert.That((string?)payload["model"], Is.EqualTo("gpt-test"));
            Assert.That((string?)payload["input"], Does.Contain("Add a yaw variable"));
            Assert.That((string?)payload["input"], Does.Contain("EDITOR CONTEXT"));
            var format = (JObject)payload["text"]!["format"]!;
            Assert.That((string?)format["type"], Is.EqualTo("json_schema"));
            Assert.That((string?)format["name"], Is.EqualTo("vizzy_patch_envelope"));
            Assert.That((bool?)format["strict"], Is.True);
            AssertStrictEnvelopeSchema((JObject)format["schema"]!);

            AssertValidResponse(result, "Added yaw");
        }

        [Test]
        public async Task Responses_normalizes_top_level_output_text_compatibility_shape()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, new JObject { ["output_text"] = ValidEnvelope("Shortcut") }.ToString(Formatting.None)));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            AssertValidResponse(result, "Shortcut");
        }

        [Test]
        public async Task Responses_normalizes_direct_envelope_compatibility_shape()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ValidEnvelope("Direct envelope")));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            AssertValidResponse(result, "Direct envelope");
        }

        [Test]
        public async Task ChatCompletions_posts_messages_and_strict_response_format_then_normalizes_choice()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ChatBody(ValidEnvelope("Changed pitch"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.ChatCompletions), CancellationToken.None);

            var request = transport.Requests.Single();
            Assert.That(request.Uri, Is.EqualTo(new Uri("https://api.example.test/openai/v1/chat/completions")));
            var payload = ParseBody(request);
            Assert.That((string?)payload["model"], Is.EqualTo("gpt-test"));
            var messages = (JArray)payload["messages"]!;
            Assert.That(messages, Has.Count.GreaterThanOrEqualTo(2));
            Assert.That(messages.Select(message => (string?)message!["role"]), Does.Contain("system"));
            Assert.That(messages.Select(message => (string?)message!["role"]), Does.Contain("user"));
            Assert.That(messages.ToString(Formatting.None), Does.Contain("Add a yaw variable"));
            Assert.That(messages.ToString(Formatting.None), Does.Contain("EDITOR CONTEXT"));
            var responseFormat = (JObject)payload["response_format"]!;
            Assert.That((string?)responseFormat["type"], Is.EqualTo("json_schema"));
            var jsonSchema = (JObject)responseFormat["json_schema"]!;
            Assert.That((string?)jsonSchema["name"], Is.EqualTo("vizzy_patch_envelope"));
            Assert.That((bool?)jsonSchema["strict"], Is.True);
            AssertStrictEnvelopeSchema((JObject)jsonSchema["schema"]!);

            AssertValidResponse(result, "Changed pitch");
        }

        [TestCase(404, "The Responses endpoint was not found")]
        [TestCase(405, "This endpoint is not supported")]
        public async Task Auto_falls_back_once_only_when_responses_endpoint_is_unavailable(int statusCode, string errorMessage)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(statusCode, ErrorBody(errorMessage, "endpoint_not_found")));
            transport.Enqueue(Response(200, ChatBody(ValidEnvelope("Fallback worked"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None);

            Assert.That(transport.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/openai/v1/responses",
                "/openai/v1/chat/completions"
            }));
            AssertValidResponse(result, "Fallback worked");
        }

        [Test]
        public void Auto_does_not_fallback_for_arbitrary_application_404()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, ErrorBody("Requested program was not found", "application_error")));

            Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Auto_never_falls_back_more_than_once_when_chat_endpoint_also_fails()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, ErrorBody("Responses endpoint not found", "endpoint_not_found")));
            transport.Enqueue(Response(405, ErrorBody("Chat endpoint not supported", "endpoint_not_supported")));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(exception!.StatusCode, Is.EqualTo(405));
            Assert.That(transport.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/openai/v1/responses",
                "/openai/v1/chat/completions"
            }));
        }

        [TestCase(401)]
        [TestCase(429)]
        [TestCase(500)]
        [TestCase(503)]
        public void Auto_does_not_fallback_or_retry_other_http_failures(int statusCode)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(statusCode, ErrorBody("Request failed", "request_failed")));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(exception!.StatusCode, Is.EqualTo(statusCode));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Transport_cancellation_propagates_without_retry_or_repair()
        {
            var transport = new FakeTransport();
            var expected = new OperationCanceledException("cancelled");
            transport.EnqueueException(expected);

            var actual = Assert.ThrowsAsync<OperationCanceledException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Transport_timeout_propagates_without_retry_or_repair()
        {
            var transport = new FakeTransport();
            var expected = new TimeoutException("timed out");
            transport.EnqueueException(expected);

            var actual = Assert.ThrowsAsync<TimeoutException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(actual, Is.SameAs(expected));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Invalid_envelope_triggers_exactly_one_repair_containing_error_and_output()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody("not-json-output")));
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Repaired envelope"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            Assert.That(transport.Requests[1].Uri, Is.EqualTo(transport.Requests[0].Uri));
            var repairInput = (string?)ParseBody(transport.Requests[1])["input"];
            Assert.That(repairInput, Does.Contain("not-json-output"));
            Assert.That(repairInput, Does.Match("(?i)(validation|invalid|parse)"));
            AssertValidResponse(result, "Repaired envelope");
        }

        [Test]
        public async Task Invalid_patch_triggers_exactly_one_repair_containing_patch_error_and_output()
        {
            var invalid = Envelope("Patch needs repair", new JArray());
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(invalid)));
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Repaired patch"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            Assert.That(transport.Requests[1].Uri, Is.EqualTo(transport.Requests[0].Uri));
            var repairInput = (string?)ParseBody(transport.Requests[1])["input"];
            Assert.That(repairInput, Does.Contain(invalid));
            Assert.That(repairInput, Does.Match("(?i)operations"));
            AssertValidResponse(result, "Repaired patch");
        }

        [Test]
        public async Task Two_invalid_outputs_return_sanitized_text_only_response()
        {
            var firstInvalid = "Authorization: Bearer bearer-secret-one\r\n{\"api_key\":\"json-secret\"}";
            var secondInvalid = "still invalid " + ApiKey;
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(firstInvalid)));
            transport.Enqueue(Response(200, ResponsesBody(secondInvalid)));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            Assert.That(result.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(result.Patch, Is.Null);
            Assert.That(result.CanApply, Is.False);
            Assert.That(result.Diagnostics, Is.Not.Empty);
            var displayText = result.Message + "\n" + string.Join("\n", result.Diagnostics);
            Assert.That(displayText, Does.Contain("[REDACTED]"));
            Assert.That(displayText, Does.Not.Contain(ApiKey));
            Assert.That(displayText, Does.Not.Contain("bearer-secret-one"));
            Assert.That(displayText, Does.Not.Contain("json-secret"));
            Assert.That(result.Diagnostics.All(IsDisplaySafeSingleLine), Is.True);
        }

        [Test]
        public void Http_error_messages_are_sanitized_before_exposure()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(
                400,
                ErrorBody("Authorization: Bearer bearer-secret and api key " + ApiKey, "bad_request")));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(exception!.Message, Does.Contain("[REDACTED]"));
            Assert.That(exception.Message, Does.Not.Contain(ApiKey));
            Assert.That(exception.Message, Does.Not.Contain("bearer-secret"));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [TestCase("https://api.example.test", "https://api.example.test/v1/responses")]
        [TestCase("https://api.example.test/", "https://api.example.test/v1/responses")]
        [TestCase("https://api.example.test/openai///", "https://api.example.test/openai/v1/responses")]
        [TestCase("http://localhost:8080", "http://localhost:8080/v1/responses")]
        [TestCase("http://127.0.0.1:8080/compat/", "http://127.0.0.1:8080/compat/v1/responses")]
        [TestCase("http://[::1]:8080", "http://[::1]:8080/v1/responses")]
        public async Task Base_uri_accepts_https_or_exact_http_loopback_and_normalizes_trailing_slashes(
            string baseUri,
            string expectedEndpoint)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Valid URI"))));

            await new OpenAiClient(transport).SendAsync(
                Request(ApiMode.Responses, new Uri(baseUri)),
                CancellationToken.None);

            Assert.That(transport.Requests.Single().Uri, Is.EqualTo(new Uri(expectedEndpoint)));
        }

        [TestCase("http://example.test")]
        [TestCase("http://localhost.example.test")]
        [TestCase("http://0.0.0.0")]
        [TestCase("ftp://localhost")]
        [TestCase("file:///tmp/api")]
        [TestCase("https://user:password@example.test")]
        [TestCase("https://example.test/api#fragment")]
        [TestCase("/relative/api")]
        public void Base_uri_rejects_unsafe_or_nonabsolute_values(string value)
        {
            var uri = new Uri(value, UriKind.RelativeOrAbsolute);

            Assert.Throws<ArgumentException>(() => Request(ApiMode.Responses, uri));
        }

        [Test]
        public void HttpTransportRequest_defensively_copies_ordinal_headers_and_preserves_utf8_json()
        {
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["X-Trace"] = "original"
            };
            var json = "{\"message\":\"\u03c0\"}";

            var request = new HttpTransportRequest(
                "POST",
                new Uri("https://example.test/v1/responses"),
                headers,
                Encoding.UTF8.GetBytes(json),
                TimeSpan.FromSeconds(1));
            headers["X-Trace"] = "changed";

            Assert.That(request.Method, Is.EqualTo("POST"));
            Assert.That(request.Uri.IsAbsoluteUri, Is.True);
            Assert.That(request.Headers["X-Trace"], Is.EqualTo("original"));
            Assert.That(request.Headers.ContainsKey("x-trace"), Is.False);
            Assert.That(Encoding.UTF8.GetString(request.Body), Is.EqualTo(json));
            Assert.That(request.Timeout, Is.EqualTo(TimeSpan.FromSeconds(1)));
        }

        [TestCase(0)]
        [TestCase(-1)]
        public void HttpTransportRequest_rejects_nonpositive_timeout(int milliseconds)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new HttpTransportRequest(
                "POST",
                new Uri("https://example.test/v1/responses"),
                new Dictionary<string, string>(),
                Encoding.UTF8.GetBytes("{}"),
                TimeSpan.FromMilliseconds(milliseconds)));
        }

        [Test]
        public void HttpTransportRequest_rejects_relative_uri()
        {
            Assert.Throws<ArgumentException>(() => new HttpTransportRequest(
                "POST",
                new Uri("v1/responses", UriKind.Relative),
                new Dictionary<string, string>(),
                Encoding.UTF8.GetBytes("{}"),
                TimeSpan.FromSeconds(1)));
        }

        private static AiRequest Request(ApiMode mode, Uri? baseUri = null)
        {
            return new AiRequest(
                mode,
                "Add a yaw variable",
                "EDITOR CONTEXT\nProgram hash: " + BaseHash,
                "gpt-test",
                baseUri ?? new Uri("https://api.example.test/openai/"),
                ApiKey,
                TimeSpan.FromSeconds(17));
        }

        private static void AssertStrictEnvelopeSchema(JObject schema)
        {
            AssertAllObjectSchemasAreStrict(schema);
            Assert.That((string?)schema["type"], Is.EqualTo("object"));
            Assert.That((bool?)schema["additionalProperties"], Is.False);
            Assert.That(schema["required"]!.Values<string>(), Is.EquivalentTo(new[] { "message", "patch" }));
            Assert.That((string?)schema["properties"]!["message"]!["type"], Is.EqualTo("string"));

            var patch = (JObject)schema["properties"]!["patch"]!;
            Assert.That((string?)patch["type"], Is.EqualTo("object"));
            Assert.That((bool?)patch["additionalProperties"], Is.False);
            Assert.That(
                patch["required"]!.Values<string>(),
                Is.EquivalentTo(new[] { "baseHash", "summary", "operations" }));
            Assert.That((string?)patch["properties"]!["baseHash"]!["pattern"], Is.EqualTo("^[0-9a-f]{64}$"));
            Assert.That((string?)patch["properties"]!["summary"]!["type"], Is.EqualTo("string"));
            Assert.That((int?)patch["properties"]!["operations"]!["minItems"], Is.EqualTo(1));

            var variants = (JArray)patch["properties"]!["operations"]!["items"]!["oneOf"]!;
            Assert.That(variants, Has.Count.EqualTo(10));
            Assert.That(variants.All(item => (bool?)item!["additionalProperties"] == false), Is.True);
            Assert.That(
                variants.Select(item => (string?)item!["properties"]!["type"]!["const"]),
                Is.EquivalentTo(new[]
                {
                    "addVariable",
                    "renameVariable",
                    "removeVariable",
                    "insertBefore",
                    "insertAfter",
                    "insertChild",
                    "replaceNode",
                    "removeNode",
                    "moveNode",
                    "updateAttribute"
                }));
        }

        private static void AssertAllObjectSchemasAreStrict(JObject schema)
        {
            var objectSchemas = schema.DescendantsAndSelf()
                .OfType<JObject>()
                .Where(candidate => string.Equals((string?)candidate["type"], "object", StringComparison.Ordinal));

            foreach (var objectSchema in objectSchemas)
            {
                Assert.That((bool?)objectSchema["additionalProperties"], Is.False);
                var properties = (JObject?)objectSchema["properties"];
                Assert.That(properties, Is.Not.Null);
                Assert.That(
                    objectSchema["required"]!.Values<string>(),
                    Is.EquivalentTo(properties!.Properties().Select(property => property.Name)));
            }
        }

        private static void AssertValidResponse(AiResponse response, string expectedMessage)
        {
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.Patch, Is.Not.Null);
            Assert.That(response.Patch!.BaseHash, Is.EqualTo(BaseHash));
            Assert.That(response.Patch.Summary, Is.EqualTo("Add yaw variable"));
            Assert.That(response.Patch.Operations, Has.Count.EqualTo(1));
            Assert.That(response.CanApply, Is.True);
            Assert.That(response.Diagnostics, Is.Empty);
        }

        private static bool IsDisplaySafeSingleLine(string value)
        {
            return value.Length <= 512 && value.All(character => !char.IsControl(character));
        }

        private static JObject ParseBody(HttpTransportRequest request)
        {
            return JObject.Parse(Encoding.UTF8.GetString(request.Body));
        }

        private static string ValidEnvelope(string message)
        {
            return Envelope(
                message,
                new JArray(new JObject
                {
                    ["type"] = "addVariable",
                    ["name"] = "yaw",
                    ["value"] = "0"
                }));
        }

        private static string Envelope(string message, JArray operations)
        {
            return new JObject
            {
                ["message"] = message,
                ["patch"] = new JObject
                {
                    ["baseHash"] = BaseHash,
                    ["summary"] = "Add yaw variable",
                    ["operations"] = operations
                }
            }.ToString(Formatting.None);
        }

        private static string ResponsesBody(string output)
        {
            return new JObject
            {
                ["output"] = new JArray(new JObject
                {
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JArray(new JObject
                    {
                        ["type"] = "output_text",
                        ["text"] = output
                    })
                })
            }.ToString(Formatting.None);
        }

        private static string ChatBody(string output)
        {
            return new JObject
            {
                ["choices"] = new JArray(new JObject
                {
                    ["index"] = 0,
                    ["message"] = new JObject
                    {
                        ["role"] = "assistant",
                        ["content"] = output
                    }
                })
            }.ToString(Formatting.None);
        }

        private static string ErrorBody(string message, string code)
        {
            return new JObject
            {
                ["error"] = new JObject
                {
                    ["message"] = message,
                    ["code"] = code
                }
            }.ToString(Formatting.None);
        }

        private static HttpTransportResponse Response(int statusCode, string body)
        {
            return new HttpTransportResponse(statusCode, Encoding.UTF8.GetBytes(body));
        }

        private sealed class FakeTransport : IAiTransport
        {
            private readonly Queue<Func<HttpTransportResponse>> responses =
                new Queue<Func<HttpTransportResponse>>();

            public List<HttpTransportRequest> Requests { get; } = new List<HttpTransportRequest>();

            public void Enqueue(HttpTransportResponse response)
            {
                responses.Enqueue(() => response);
            }

            public void EnqueueException(Exception exception)
            {
                responses.Enqueue(() => throw exception);
            }

            public Task<HttpTransportResponse> SendAsync(
                HttpTransportRequest request,
                CancellationToken cancellationToken)
            {
                Requests.Add(request);
                if (responses.Count == 0)
                {
                    throw new InvalidOperationException("No fake response was queued.");
                }

                return Task.FromResult(responses.Dequeue()());
            }
        }
    }

    public sealed class ContextBuilderTests
    {
        private const string ApiKey = "sk-context-secret";
        private const int MaximumContextBytes = 64 * 1024;

        [Test]
        public void Editor_context_includes_declarations_and_complete_canonical_xml_at_or_below_limit()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program z='2' a='1'><Variables><Variable name='pitch' /></Variables>" +
                "<Instructions><Event id='1' event='FlightStart' /></Instructions><Expressions /></Program>");

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch\r\nCustom node: guidance",
                selection: null);

            Assert.That(context, Does.Contain("Variable: pitch\nCustom node: guidance"));
            Assert.That(context, Does.Contain(document.ToXml()));
            Assert.That(context, Does.Not.Contain("\r"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Large_editor_context_excludes_full_xml_but_includes_summaries_and_selected_id_subtree()
        {
            var document = LargeDocument(selectedPayloadBytes: 256);

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch\nCustom node: guidance",
                new NodeSelector(1, null));

            Assert.That(Encoding.UTF8.GetByteCount(document.ToXml()), Is.GreaterThan(MaximumContextBytes));
            Assert.That(context, Does.Contain("Variable: pitch"));
            Assert.That(context, Does.Contain("FlightStart"));
            Assert.That(context, Does.Contain("guidance"));
            Assert.That(context, Does.Contain("selected-marker"));
            Assert.That(context, Does.Not.Contain(document.ToXml()));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Large_editor_context_selects_subtree_by_canonical_path()
        {
            var document = LargeDocument(selectedPayloadBytes: 256);

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(null, "/Program[0]/Instructions[0]/Event[0]/Instructions[0]/Log[0]"));

            Assert.That(context, Does.Contain("selected-marker"));
            Assert.That(context, Does.Not.Contain("bulk-last-marker"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Missing_editor_selection_is_reported_deterministically()
        {
            var document = LargeDocument(selectedPayloadBytes: 256);
            var builder = new ContextBuilder(ApiKey);

            var first = builder.BuildEditorContext(document, "Variable: pitch", new NodeSelector(999999, null));
            var second = builder.BuildEditorContext(document, "Variable: pitch", new NodeSelector(999999, null));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Contain("Selection: missing"));
            Assert.That(Encoding.UTF8.GetByteCount(first), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Ambiguous_editor_id_selection_is_reported_without_choosing_a_subtree()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions>" +
                "<Event id='7' event='First'><Instructions><Log text='first-choice' /></Instructions></Event>" +
                "<Event id='7' event='Second'><Instructions><Log text='second-choice' /></Instructions></Event>" +
                new string(' ', MaximumContextBytes) +
                "</Instructions><Expressions /></Program>");

            var builder = new ContextBuilder(ApiKey);
            var context = builder.BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(7, null));
            var repeated = builder.BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(7, null));

            Assert.That(context, Is.EqualTo(repeated));
            Assert.That(context, Does.Contain("Selection: ambiguous"));
            Assert.That(context, Does.Not.Contain("first-choice"));
            Assert.That(context, Does.Not.Contain("second-choice"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Oversized_selected_subtree_is_truncated_with_a_stable_marker_and_bounded()
        {
            var document = LargeDocument(selectedPayloadBytes: MaximumContextBytes * 2);

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(1, null));

            Assert.That(context, Does.Contain("[TRUNCATED]"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Editor_context_normalizes_line_endings_and_redacts_secrets()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions><Log text='Authorization: Bearer xml-secret' />" +
                "</Instructions><Expressions /></Program>");

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Configured " + ApiKey + "\r\n{\"api_key\":\"json-secret\"}",
                selection: null);

            Assert.That(context, Does.Contain("[REDACTED]"));
            Assert.That(context, Does.Not.Contain(ApiKey));
            Assert.That(context, Does.Not.Contain("xml-secret"));
            Assert.That(context, Does.Not.Contain("json-secret"));
            Assert.That(context, Does.Not.Contain("\r"));
        }

        [Test]
        public void Flight_context_keeps_only_200_most_recent_logs_in_chronological_order()
        {
            var logs = Enumerable.Range(0, 205)
                .Select(index => new FlightLogEntry(Utc(index), "log-" + index.ToString("D3")))
                .Reverse()
                .ToArray();

            var context = new ContextBuilder(ApiKey).BuildFlightContext(
                logs,
                Array.Empty<TelemetrySample>());

            Assert.That(context, Does.Not.Contain("log-000"));
            Assert.That(context, Does.Not.Contain("log-004"));
            Assert.That(context, Does.Contain("log-005"));
            Assert.That(context, Does.Contain("log-204"));
            Assert.That(context.IndexOf("log-005", StringComparison.Ordinal),
                Is.LessThan(context.IndexOf("log-204", StringComparison.Ordinal)));
        }

        [Test]
        public void Flight_context_summarizes_last_120_samples_by_ordinal_metric_with_min_max_latest()
        {
            var telemetry = Enumerable.Range(0, 125)
                .Select(index => new TelemetrySample(
                    Utc(index),
                    new Dictionary<string, double>
                    {
                        ["Speed"] = index * 2,
                        ["Altitude"] = index
                    }))
                .Reverse()
                .ToArray();

            var context = new ContextBuilder(ApiKey).BuildFlightContext(
                Array.Empty<FlightLogEntry>(),
                telemetry);

            Assert.That(context, Does.Contain("Altitude: min=5 max=124 latest=124"));
            Assert.That(context, Does.Contain("Speed: min=10 max=248 latest=248"));
            Assert.That(context.IndexOf("Altitude:", StringComparison.Ordinal),
                Is.LessThan(context.IndexOf("Speed:", StringComparison.Ordinal)));
        }

        [Test]
        public void Flight_context_is_bounded_normalized_deterministic_and_secret_free()
        {
            var logs = Enumerable.Range(0, 300)
                .Select(index => new FlightLogEntry(
                    Utc(index),
                    "log " + index + "\r\nAuthorization: Bearer flight-secret " + ApiKey + new string('x', 1024)))
                .Reverse()
                .ToArray();
            var telemetry = Enumerable.Range(0, 150)
                .Select(index => new TelemetrySample(
                    Utc(index),
                    new Dictionary<string, double> { ["Metric" + index.ToString("D3")] = index }))
                .ToArray();
            var builder = new ContextBuilder(ApiKey);

            var first = builder.BuildFlightContext(logs, telemetry);
            var second = builder.BuildFlightContext(logs, telemetry);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(Encoding.UTF8.GetByteCount(first), Is.LessThanOrEqualTo(MaximumContextBytes));
            Assert.That(first, Does.Contain("[REDACTED]"));
            Assert.That(first, Does.Not.Contain(ApiKey));
            Assert.That(first, Does.Not.Contain("flight-secret"));
            Assert.That(first, Does.Not.Contain("\r"));
        }

        private static DateTime Utc(int minutes)
        {
            return new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);
        }

        private static VizzyProgramDocument LargeDocument(int selectedPayloadBytes)
        {
            var bulk = string.Concat(Enumerable.Range(0, 1400).Select(index =>
                "<Log id='" + (1000 + index) + "' text='bulk-" + index.ToString("D4") +
                (index == 1399 ? "-bulk-last-marker" : string.Empty) + new string('x', 32) + "' />"));
            var selectedPayload = new string('s', selectedPayloadBytes);
            return VizzyProgramDocument.Parse(
                "<Program><Variables><Variable name='pitch' /></Variables><Instructions>" +
                "<Event id='1' event='FlightStart'><Instructions>" +
                "<Log id='2' text='selected-marker-" + selectedPayload + "' />" +
                "</Instructions></Event>" + bulk +
                "</Instructions><Expressions><CustomNode id='3' name='guidance'><Constant number='1' />" +
                "</CustomNode></Expressions></Program>");
        }
    }
}
