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
        private const string ProtectedRootInstruction =
            "Never add, remove, replace, or move the direct Program containers Variables, " +
            "Instructions, or Expressions. Modify only their permitted descendants.";

        [TestCase(ApiMode.Responses)]
        [TestCase(ApiMode.ChatCompletions)]
        public async Task Endpoint_system_instruction_protects_direct_program_containers(ApiMode mode)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ModelBody(mode, ValidEnvelope("Protected roots"))));

            await new OpenAiClient(transport).SendAsync(Request(mode), CancellationToken.None);

            var payload = ParseBody(transport.Requests.Single());
            var instruction = mode == ApiMode.Responses
                ? (string?)payload["input"]
                : (string?)((JArray)payload["messages"]!)[0]!["content"];
            Assert.That(instruction, Does.Contain(ProtectedRootInstruction));
        }

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

        [Test]
        public async Task AddVariable_nullable_wire_value_normalizes_to_omitted_domain_value()
        {
            var output = Envelope(
                "Add yaw with default value",
                new JArray(new JObject
                {
                    ["type"] = "addVariable",
                    ["name"] = "yaw",
                    ["value"] = JValue.CreateNull()
                }));
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(output)));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            var schema = (JObject)ParseBody(transport.Requests.Single())["text"]!["format"]!["schema"]!;
            var addVariable = OperationVariants(schema).Single(variant =>
                string.Equals((string?)variant["properties"]!["type"]!["const"], "addVariable", StringComparison.Ordinal));
            var valueSchema = (JObject)addVariable["properties"]!["value"]!;
            Assert.That(valueSchema["anyOf"]!.Select(member => (string?)member!["type"]),
                Is.EquivalentTo(new[] { "string", "null" }));
            Assert.That(addVariable["required"]!.Values<string>(), Does.Contain("value"));
            Assert.That(result.Patch!.Operations.Single().Value, Is.Null);
            Assert.That(JsonConvert.SerializeObject(result.Patch), Does.Not.Contain("\"value\""));
            Assert.That(result.CanApply, Is.True);
        }

        [Test]
        public async Task NodeSpec_wire_attributes_and_recursive_children_normalize_and_apply()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions><Log id='1' text='anchor' /></Instructions><Expressions /></Program>");
            var node = WireNode(
                "Log",
                new[] { ("id", "2"), ("text", "inserted") },
                WireNode("Constant", new[] { ("number", "1") }));
            var output = Envelope(
                "Insert attributed node",
                VizzyProgramHash.Compute(document),
                new JArray(new JObject
                {
                    ["type"] = "insertAfter",
                    ["target"] = new JObject { ["id"] = 1 },
                    ["node"] = node
                }));
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(output)));

            var response = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            var operation = response.Patch!.Operations.Single();
            Assert.That(operation.Node!.Attributes, Is.EqualTo(new Dictionary<string, string>
            {
                ["id"] = "2",
                ["text"] = "inserted"
            }));
            Assert.That(operation.Node.Children.Single().Attributes["number"], Is.EqualTo("1"));
            var applied = VizzyPatchEngine.Apply(document, response.Patch);
            Assert.That(applied.Document.FindById(2)!.Attribute("text")!.Value, Is.EqualTo("inserted"));
            Assert.That(applied.Document.FindById(2)!.Element("Constant")!.Attribute("number")!.Value, Is.EqualTo("1"));
        }

        [Test]
        public async Task Duplicate_NodeSpec_wire_attribute_names_trigger_one_patch_repair()
        {
            var duplicateNode = WireNode("Log", new[] { ("text", "first"), ("text", "second") });
            var invalid = Envelope(
                "Insert invalid node",
                new JArray(new JObject
                {
                    ["type"] = "insertAfter",
                    ["target"] = new JObject { ["id"] = 1 },
                    ["node"] = duplicateNode
                }));
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(invalid)));
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Repaired duplicate attribute"))));

            var response = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            var repairInput = (string?)ParseBody(transport.Requests[1])["input"];
            Assert.That(repairInput, Does.Contain(invalid));
            Assert.That(repairInput, Does.Match("(?i)duplicate.*attribute"));
            AssertValidResponse(response, "Repaired duplicate attribute");
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

        [TestCase(404, "endpoint_not_found")]
        [TestCase(405, "endpoint_not_supported")]
        public async Task Auto_falls_back_for_structured_endpoint_code_with_terse_message(int statusCode, string code)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(statusCode, ErrorBody("No", code)));
            transport.Enqueue(Response(200, ChatBody(ValidEnvelope("Structured fallback"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None);

            Assert.That(transport.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/openai/v1/responses",
                "/openai/v1/chat/completions"
            }));
            AssertValidResponse(result, "Structured fallback");
        }

        [Test]
        public void Auto_does_not_fallback_when_unavailability_words_describe_a_model()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(
                404,
                ErrorBody("Responses endpoint is available; model not found", "model_not_found")));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(exception!.StatusCode, Is.EqualTo(404));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Auto_does_not_fallback_when_plain_text_says_endpoint_works_but_model_is_missing()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, "Responses endpoint works; requested model not found"));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(exception!.StatusCode, Is.EqualTo(404));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task Auto_textual_fallback_requires_clear_responses_endpoint_unavailability()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(405, "Cannot POST /v1/responses; Responses endpoint unavailable"));
            transport.Enqueue(Response(200, ChatBody(ValidEnvelope("Text fallback"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            AssertValidResponse(result, "Text fallback");
        }

        [Test]
        public async Task Auto_fallback_then_extracted_model_repair_is_bounded_to_three_requests()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, ErrorBody("No", "endpoint_not_found")));
            transport.Enqueue(Response(200, ChatBody("invalid-envelope")));
            transport.Enqueue(Response(200, ChatBody(ValidEnvelope("Fallback repair"))));

            var result = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None);

            Assert.That(transport.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/openai/v1/responses",
                "/openai/v1/chat/completions",
                "/openai/v1/chat/completions"
            }));
            AssertValidResponse(result, "Fallback repair");
        }

        [Test]
        public void Auto_does_not_use_unrelated_textual_not_found_words()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, "Requested model was not found"));

            var exception = Assert.ThrowsAsync<OpenAiApiException>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(exception!.StatusCode, Is.EqualTo(404));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
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
        public void Arbitrary_transport_exception_is_replaced_with_safe_redacted_exception()
        {
            var transport = new FakeTransport();
            var raw = new InvalidOperationException(
                "Authorization: Bearer transport-secret; configured=" + ApiKey);
            transport.EnqueueException(raw);

            var exposed = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(exposed, Is.Not.SameAs(raw));
            AssertSafeException(exposed!, ApiKey, "transport-secret");
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
        }

        [Test]
        public void Malformed_responses_http_200_wrapper_does_not_trigger_model_repair()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, "{\"output\":[{\"debug\":\"" + ApiKey + "\"}"));

            var exception = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            AssertSafeException(exception!, ApiKey);
        }

        [Test]
        public void Missing_responses_assistant_output_does_not_trigger_model_repair()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, new JObject
            {
                ["output"] = new JArray(),
                ["debug"] = ApiKey
            }.ToString(Formatting.None)));

            var exception = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            AssertSafeException(exception!, ApiKey);
        }

        [TestCase("message", "user")]
        [TestCase("function_call", "assistant")]
        public void Wrong_responses_item_type_or_role_is_protocol_failure_without_repair(string itemType, string role)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ResponsesBody(ValidEnvelope("Must not extract"), itemType, role)));

            var exception = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None));

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            AssertSafeException(exception!);
        }

        [Test]
        public void Malformed_chat_wrapper_does_not_trigger_model_repair()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, new JObject
            {
                ["choices"] = new JObject(),
                ["api_key"] = ApiKey
            }.ToString(Formatting.None)));

            var exception = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.ChatCompletions), CancellationToken.None));

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            AssertSafeException(exception!, ApiKey);
        }

        [Test]
        public async Task Official_responses_refusal_returns_text_only_result_without_repair()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, RefusalBody("I cannot provide that change.")));

            var response = await new OpenAiClient(transport).SendAsync(Request(ApiMode.Responses), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            Assert.That(response.Message, Is.EqualTo("I cannot provide that change."));
            Assert.That(response.Patch, Is.Null);
            Assert.That(response.CanApply, Is.False);
            Assert.That(response.Diagnostics.All(IsDisplaySafeSingleLine), Is.True);
        }

        [Test]
        public async Task Official_chat_refusal_returns_text_only_result_without_repair()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, new JObject
            {
                ["choices"] = new JArray(new JObject
                {
                    ["message"] = new JObject
                    {
                        ["role"] = "assistant",
                        ["refusal"] = "I cannot provide that change."
                    }
                })
            }.ToString(Formatting.None)));

            var response = await new OpenAiClient(transport).SendAsync(
                Request(ApiMode.ChatCompletions),
                CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            Assert.That(response.Message, Is.EqualTo("I cannot provide that change."));
            Assert.That(response.Patch, Is.Null);
            Assert.That(response.CanApply, Is.False);
            Assert.That(response.Diagnostics.All(IsDisplaySafeSingleLine), Is.True);
        }

        [TestCase(ApiMode.Responses)]
        [TestCase(ApiMode.ChatCompletions)]
        public async Task Extracted_invalid_model_text_and_only_that_text_consumes_one_repair(ApiMode mode)
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(200, ModelBody(mode, "extracted-invalid-envelope")));
            transport.Enqueue(Response(200, ModelBody(mode, ValidEnvelope("Extracted repair"))));

            var response = await new OpenAiClient(transport).SendAsync(Request(mode), CancellationToken.None);

            Assert.That(transport.Requests, Has.Count.EqualTo(2));
            Assert.That(transport.Requests[1].Uri, Is.EqualTo(transport.Requests[0].Uri));
            Assert.That(RequestInput(transport.Requests[1], mode), Does.Contain("extracted-invalid-envelope"));
            AssertValidResponse(response, "Extracted repair");
        }

        [Test]
        public void Auto_fallback_then_malformed_chat_wrapper_stops_after_two_requests()
        {
            var transport = new FakeTransport();
            transport.Enqueue(Response(404, ErrorBody("No", "endpoint_not_found")));
            transport.Enqueue(Response(200, "{\"choices\":[]}"));

            var exception = Assert.CatchAsync<Exception>(
                async () => await new OpenAiClient(transport).SendAsync(Request(ApiMode.Auto), CancellationToken.None));

            Assert.That(transport.Requests.Select(request => request.Uri.AbsolutePath), Is.EqualTo(new[]
            {
                "/openai/v1/responses",
                "/openai/v1/chat/completions"
            }));
            AssertSafeException(exception!);
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
        [TestCase("http://127.1")]
        [TestCase("http://0177.0.0.1")]
        [TestCase("http://0x7f.0.0.1")]
        [TestCase("http://2130706433")]
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
            AssertAllAnyOfMembersAreSchemas(schema);
            Assert.That(
                schema.DescendantsAndSelf().OfType<JProperty>().Where(property =>
                    string.Equals(property.Name, "oneOf", StringComparison.Ordinal)),
                Is.Empty);
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

            var variants = OperationVariants(schema);
            Assert.That(variants, Has.Count.EqualTo(10));
            Assert.That(variants.All(item => (bool?)item!["additionalProperties"] == false), Is.True);
            var expectedFields = new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["addVariable"] = new[] { "type", "name", "value" },
                ["renameVariable"] = new[] { "type", "name", "newName" },
                ["removeVariable"] = new[] { "type", "name" },
                ["insertBefore"] = new[] { "type", "target", "node" },
                ["insertAfter"] = new[] { "type", "target", "node" },
                ["insertChild"] = new[] { "type", "target", "node" },
                ["replaceNode"] = new[] { "type", "target", "node" },
                ["removeNode"] = new[] { "type", "target" },
                ["moveNode"] = new[] { "type", "target", "destination" },
                ["updateAttribute"] = new[] { "type", "target", "attribute", "value" }
            };
            Assert.That(
                variants.Select(item => (string?)item!["properties"]!["type"]!["const"]),
                Is.EquivalentTo(expectedFields.Keys));
            foreach (var variant in variants.OfType<JObject>())
            {
                var properties = (JObject)variant["properties"]!;
                var operationType = (string)properties["type"]!["const"]!;
                Assert.That((string?)properties["type"]!["type"], Is.EqualTo("string"));
                Assert.That(properties.Properties().Select(property => property.Name),
                    Is.EquivalentTo(expectedFields[operationType]));
                Assert.That(variant["required"]!.Values<string>(),
                    Is.EquivalentTo(expectedFields[operationType]));
            }

            var addVariable = variants.OfType<JObject>().Single(variant =>
                string.Equals((string?)variant["properties"]!["type"]!["const"], "addVariable", StringComparison.Ordinal));
            Assert.That(
                addVariable["properties"]!["value"]!["anyOf"]!.Select(member => (string?)member!["type"]),
                Is.EquivalentTo(new[] { "string", "null" }));

            var anyOfArrays = schema.DescendantsAndSelf()
                .OfType<JProperty>()
                .Where(property => string.Equals(property.Name, "anyOf", StringComparison.Ordinal))
                .Select(property => (JArray)property.Value)
                .ToArray();
            Assert.That(anyOfArrays.Any(IsSelectorUnion), Is.True);

            var nodeSpec = (JObject)schema["$defs"]!["nodeSpec"]!;
            var attributes = (JObject)nodeSpec["properties"]!["attributes"]!;
            Assert.That((string?)attributes["type"], Is.EqualTo("array"));
            var attributeItem = (JObject)attributes["items"]!;
            Assert.That((string?)attributeItem["type"], Is.EqualTo("object"));
            Assert.That(attributeItem["required"]!.Values<string>(), Is.EquivalentTo(new[] { "name", "value" }));
            Assert.That(attributeItem["properties"]!.Children<JProperty>().Select(property => property.Name),
                Is.EquivalentTo(new[] { "name", "value" }));
            Assert.That((string?)attributeItem["properties"]!["name"]!["type"], Is.EqualTo("string"));
            Assert.That((string?)attributeItem["properties"]!["value"]!["type"], Is.EqualTo("string"));
            Assert.That((string?)nodeSpec["properties"]!["children"]!["items"]!["$ref"],
                Is.EqualTo("#/$defs/nodeSpec"));
        }

        private static JArray OperationVariants(JObject schema)
        {
            return (JArray)schema["properties"]!["patch"]!["properties"]!["operations"]!["items"]!["anyOf"]!;
        }

        private static void AssertAllObjectSchemasAreStrict(JObject schema)
        {
            var objectSchemas = schema.DescendantsAndSelf()
                .OfType<JObject>()
                .Where(candidate =>
                    candidate["type"]?.Type == JTokenType.String &&
                    string.Equals(candidate["type"]!.Value<string>(), "object", StringComparison.Ordinal));

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

        private static void AssertAllAnyOfMembersAreSchemas(JObject schema)
        {
            var unions = schema.DescendantsAndSelf()
                .OfType<JProperty>()
                .Where(property => string.Equals(property.Name, "anyOf", StringComparison.Ordinal))
                .Select(property => property.Value)
                .ToArray();
            Assert.That(unions, Is.Not.Empty);
            foreach (var union in unions)
            {
                Assert.That(union, Is.TypeOf<JArray>());
                Assert.That(union, Is.Not.Empty);
                foreach (var member in union.Children())
                {
                    Assert.That(member, Is.TypeOf<JObject>());
                    Assert.That(
                        member["type"] != null || member["$ref"] != null ||
                        member["const"] != null || member["anyOf"] != null,
                        Is.True);
                }
            }
        }

        private static bool IsSelectorUnion(JArray union)
        {
            if (union.Count != 2 || union.Any(member => member["type"]?.Value<string>() != "object"))
            {
                return false;
            }

            var byRequiredField = union.ToDictionary(
                member => member["required"]!.Values<string>().Single()!,
                member => member,
                StringComparer.Ordinal);
            return byRequiredField.TryGetValue("id", out var idVariant) &&
                byRequiredField.TryGetValue("path", out var pathVariant) &&
                string.Equals((string?)idVariant["properties"]!["id"]!["type"], "integer", StringComparison.Ordinal) &&
                string.Equals((string?)pathVariant["properties"]!["path"]!["type"], "string", StringComparison.Ordinal);
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

        private static void AssertSafeException(Exception exception, params string[] secrets)
        {
            Assert.That(exception, Is.Not.TypeOf<OperationCanceledException>());
            Assert.That(exception, Is.Not.TypeOf<TimeoutException>());
            Assert.That(exception.Message, Is.Not.Null.And.Not.Empty);
            var exposed = exception.Message + "\n" + exception;
            foreach (var secret in secrets)
            {
                Assert.That(exposed, Does.Not.Contain(secret));
            }
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
            return Envelope(message, BaseHash, operations);
        }

        private static string Envelope(string message, string baseHash, JArray operations)
        {
            return new JObject
            {
                ["message"] = message,
                ["patch"] = new JObject
                {
                    ["baseHash"] = baseHash,
                    ["summary"] = "Add yaw variable",
                    ["operations"] = operations
                }
            }.ToString(Formatting.None);
        }

        private static string ResponsesBody(string output)
        {
            return ResponsesBody(output, "message", "assistant");
        }

        private static string ResponsesBody(string output, string itemType, string role)
        {
            return new JObject
            {
                ["output"] = new JArray(new JObject
                {
                    ["type"] = itemType,
                    ["role"] = role,
                    ["content"] = new JArray(new JObject
                    {
                        ["type"] = "output_text",
                        ["text"] = output
                    })
                })
            }.ToString(Formatting.None);
        }

        private static string RefusalBody(string refusal)
        {
            return new JObject
            {
                ["output"] = new JArray(new JObject
                {
                    ["type"] = "message",
                    ["role"] = "assistant",
                    ["content"] = new JArray(new JObject
                    {
                        ["type"] = "refusal",
                        ["refusal"] = refusal
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

        private static string ModelBody(ApiMode mode, string output)
        {
            return mode == ApiMode.Responses ? ResponsesBody(output) : ChatBody(output);
        }

        private static string RequestInput(HttpTransportRequest request, ApiMode mode)
        {
            var payload = ParseBody(request);
            if (mode == ApiMode.Responses)
            {
                return (string)payload["input"]!;
            }

            var messages = (JArray)payload["messages"]!;
            return (string)messages[messages.Count - 1]!["content"]!;
        }

        private static JObject WireNode(
            string element,
            IEnumerable<(string Name, string Value)> attributes,
            params JObject[] children)
        {
            return new JObject
            {
                ["element"] = element,
                ["attributes"] = new JArray(attributes.Select(attribute => new JObject
                {
                    ["name"] = attribute.Name,
                    ["value"] = attribute.Value
                })),
                ["children"] = new JArray(children)
            };
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
            Assert.That(context, Does.Contain(document.ToXml()));
            Assert.That(context, Does.Contain("first-choice"));
            Assert.That(context, Does.Contain("second-choice"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Small_editor_context_with_ambiguous_id_keeps_complete_canonical_xml()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions>" +
                "<Event id='7' event='First' /><Event id='7' event='Second' />" +
                "</Instructions><Expressions /></Program>");

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(7, null));

            Assert.That(context, Does.Contain(document.ToXml()));
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
        public void Editor_context_size_decision_uses_final_canonical_context_not_discarded_source_whitespace()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program>" + new string(' ', MaximumContextBytes + 1024) +
                "<Variables /><Instructions><Event id='1' event='FlightStart' /></Instructions>" +
                "<Expressions /></Program>");
            Assert.That(Encoding.UTF8.GetByteCount(document.ToXml()), Is.LessThan(MaximumContextBytes));

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "Variable: pitch",
                new NodeSelector(1, null));

            Assert.That(context, Does.Contain("Program XML:\n" + document.ToXml()));
            Assert.That(context, Does.Not.Contain("Root summaries:"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Huge_declarations_preserve_bounded_section_summaries_and_selected_subtree()
        {
            var document = LargeDocument(selectedPayloadBytes: 128);
            var declarations = "declaration-head\n" + new string('d', MaximumContextBytes * 2) + "\ndeclaration-tail";

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                declarations,
                new NodeSelector(1, null));

            Assert.That(context, Does.Contain("Declarations:"));
            Assert.That(context, Does.Contain("declaration-head"));
            Assert.That(context, Does.Contain("[TRUNCATED]"));
            Assert.That(context, Does.Contain("Root summaries:"));
            Assert.That(context, Does.Contain("FlightStart"));
            Assert.That(context, Does.Contain("guidance"));
            Assert.That(context, Does.Contain("Selection: matched"));
            Assert.That(context, Does.Contain("selected-marker"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Html_escaped_api_key_value_is_redacted_before_editor_context_exposure()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions>" +
                "<Log text='&quot;api_key&quot;: &quot;xml-secret&quot;' />" +
                "</Instructions><Expressions /></Program>");

            var context = new ContextBuilder(ApiKey).BuildEditorContext(
                document,
                "&quot;api_key&quot;: &quot;html-secret&quot;",
                selection: null);

            Assert.That(context, Does.Contain("[REDACTED]"));
            Assert.That(context, Does.Not.Contain("xml-secret"));
            Assert.That(context, Does.Not.Contain("html-secret"));
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

        [Test]
        public void Huge_flight_context_preserves_newest_logs_when_truncated()
        {
            var logs = Enumerable.Range(0, 200)
                .Select(index => new FlightLogEntry(
                    Utc(index),
                    "log-" + index.ToString("D3") +
                    (index == 199 ? "-newest" : string.Empty) + new string('x', 1024)))
                .ToArray();

            var context = new ContextBuilder(ApiKey).BuildFlightContext(
                logs,
                Array.Empty<TelemetrySample>());

            Assert.That(context, Does.Contain("log-199-newest"));
            Assert.That(context, Does.Contain("[TRUNCATED]"));
            Assert.That(Encoding.UTF8.GetByteCount(context), Is.LessThanOrEqualTo(MaximumContextBytes));
        }

        [Test]
        public void Nonfinite_telemetry_is_omitted_deterministically_without_nonfinite_tokens()
        {
            var telemetry = new[]
            {
                new TelemetrySample(Utc(0), new Dictionary<string, double> { ["Metric"] = 5 }),
                new TelemetrySample(Utc(1), new Dictionary<string, double>
                {
                    ["Metric"] = double.NaN,
                    ["OnlyBad"] = double.PositiveInfinity
                }),
                new TelemetrySample(Utc(2), new Dictionary<string, double>
                {
                    ["Metric"] = double.NegativeInfinity,
                    ["OnlyBad"] = double.NaN
                }),
                new TelemetrySample(Utc(3), new Dictionary<string, double> { ["Metric"] = 7 })
            };
            var builder = new ContextBuilder(ApiKey);

            var first = builder.BuildFlightContext(Array.Empty<FlightLogEntry>(), telemetry);
            var second = builder.BuildFlightContext(Array.Empty<FlightLogEntry>(), telemetry);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(first, Does.Contain("Metric: min=5 max=7 latest=7"));
            Assert.That(first, Does.Not.Contain("OnlyBad"));
            Assert.That(first, Does.Not.Match("(?i)nan|infinity"));
            Assert.That(Encoding.UTF8.GetByteCount(first), Is.LessThanOrEqualTo(MaximumContextBytes));
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
