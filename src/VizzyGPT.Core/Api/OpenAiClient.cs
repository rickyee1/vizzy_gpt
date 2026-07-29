using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Security;

namespace VizzyGPT.Core.Api
{
    public sealed class OpenAiApiException : Exception
    {
        public OpenAiApiException(int statusCode, string message)
            : base(message)
        {
            StatusCode = statusCode;
        }

        public int StatusCode { get; }
    }

    public sealed class OpenAiClient
    {
        private const string EnvelopeName = "vizzy_patch_envelope";
        private const int MaximumDiagnosticLength = 512;
        private const string ModelInstruction =
            "Return only a JSON object matching the supplied Vizzy patch envelope schema. " +
            "Never add, remove, replace, or move the direct Program containers Variables, " +
            "Instructions, or Expressions. Modify only their permitted descendants.";

        private readonly IAiTransport transport;

        public OpenAiClient(IAiTransport transport)
        {
            this.transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public async Task<AiResponse> SendAsync(AiRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            var endpointMode = request.Mode == ApiMode.ChatCompletions
                ? ApiMode.ChatCompletions
                : ApiMode.Responses;
            var initialInput = BuildInitialInput(request.Prompt, request.Context);
            var response = await SendToEndpointAsync(request, endpointMode, initialInput, cancellationToken).ConfigureAwait(false);

            if (request.Mode == ApiMode.Auto &&
                response.StatusCode >= 400 &&
                IsResponsesEndpointUnavailable(response.StatusCode, DecodeBody(response)))
            {
                endpointMode = ApiMode.ChatCompletions;
                response = await SendToEndpointAsync(request, endpointMode, initialInput, cancellationToken).ConfigureAwait(false);
            }

            EnsureSuccess(response, request.ApiKey);
            var firstBody = DecodeBody(response);
            var firstExtraction = ExtractModelResponse(endpointMode, firstBody, request.ApiKey);
            if (firstExtraction.Refusal != null)
            {
                return new AiResponse(
                    MakeDisplaySafe(firstExtraction.Refusal, request.ApiKey),
                    patch: null,
                    canApply: false,
                    Array.Empty<string>(),
                    firstExtraction.Metadata);
            }

            var invalidOutput = firstExtraction.ModelOutput!;
            if (TryParseEnvelope(
                invalidOutput,
                firstExtraction.Metadata,
                out var validResponse,
                out var validationError))
            {
                return validResponse;
            }

            var repairInput = BuildRepairInput(invalidOutput, validationError);
            var repairResponse = await SendToEndpointAsync(request, endpointMode, repairInput, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(repairResponse, request.ApiKey);

            var repairBody = DecodeBody(repairResponse);
            var repairExtraction = ExtractModelResponse(endpointMode, repairBody, request.ApiKey);
            if (repairExtraction.Refusal != null)
            {
                return new AiResponse(
                    MakeDisplaySafe(repairExtraction.Refusal, request.ApiKey),
                    patch: null,
                    canApply: false,
                    Array.Empty<string>(),
                    repairExtraction.Metadata);
            }

            invalidOutput = repairExtraction.ModelOutput!;
            var repairMetadata = WithSchemaRepair(repairExtraction.Metadata);
            if (TryParseEnvelope(invalidOutput, repairMetadata, out validResponse, out validationError))
            {
                return validResponse;
            }

            var diagnostic = MakeDisplaySafe(
                "Model output remained invalid after one schema repair. " + validationError +
                " Output: " + invalidOutput,
                request.ApiKey);
            return new AiResponse(
                "The model response could not be validated.",
                patch: null,
                canApply: false,
                new[] { diagnostic },
                repairExtraction.Metadata);
        }

        private async Task<HttpTransportResponse> SendToEndpointAsync(
            AiRequest request,
            ApiMode endpointMode,
            string input,
            CancellationToken cancellationToken)
        {
            var endpoint = endpointMode == ApiMode.Responses ? "/v1/responses" : "/v1/chat/completions";
            var uri = new Uri(request.BaseUri.AbsoluteUri.TrimEnd('/') + endpoint, UriKind.Absolute);
            var payload = endpointMode == ApiMode.Responses
                ? CreateResponsesPayload(request.Model, input)
                : CreateChatPayload(request.Model, input);
            var headers = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Authorization"] = "Bearer " + request.ApiKey,
                ["Content-Type"] = "application/json"
            };
            var transportRequest = new HttpTransportRequest(
                "POST",
                uri,
                headers,
                Encoding.UTF8.GetBytes(payload.ToString(Formatting.None)),
                request.Timeout);

            try
            {
                return await transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException("The AI transport returned a null response.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "AI transport failed: " + MakeDisplaySafe(exception.Message, request.ApiKey));
            }
        }

        private static JObject CreateResponsesPayload(string model, string input)
        {
            return new JObject
            {
                ["model"] = model,
                ["input"] = ModelInstruction + "\n\n" + input,
                ["text"] = new JObject
                {
                    ["format"] = CreateSchemaFormat()
                }
            };
        }

        private static JObject CreateChatPayload(string model, string input)
        {
            return new JObject
            {
                ["model"] = model,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "system",
                        ["content"] = ModelInstruction
                    },
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = input
                    }
                },
                ["response_format"] = new JObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JObject
                    {
                        ["name"] = EnvelopeName,
                        ["strict"] = true,
                        ["schema"] = CreateEnvelopeSchema()
                    }
                }
            };
        }

        private static JObject CreateSchemaFormat()
        {
            return new JObject
            {
                ["type"] = "json_schema",
                ["name"] = EnvelopeName,
                ["strict"] = true,
                ["schema"] = CreateEnvelopeSchema()
            };
        }

        private static JObject CreateEnvelopeSchema()
        {
            var schema = StrictObject(
                ("message", StringSchema()),
                ("patch", StrictObject(
                    ("baseHash", new JObject
                    {
                        ["type"] = "string",
                        ["pattern"] = "^[0-9a-f]{64}$"
                    }),
                    ("summary", StringSchema()),
                    ("operations", new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = new JObject
                        {
                            ["anyOf"] = new JArray(
                                OperationSchema("addVariable", ("name", StringSchema()), ("value", NullableStringSchema())),
                                OperationSchema("renameVariable", ("name", StringSchema()), ("newName", StringSchema())),
                                OperationSchema("removeVariable", ("name", StringSchema())),
                                OperationSchema("insertBefore", ("target", SelectorSchema()), ("node", DefinitionReference("nodeSpec"))),
                                OperationSchema("insertAfter", ("target", SelectorSchema()), ("node", DefinitionReference("nodeSpec"))),
                                OperationSchema("insertChild", ("target", SelectorSchema()), ("node", DefinitionReference("nodeSpec"))),
                                OperationSchema("replaceNode", ("target", SelectorSchema()), ("node", DefinitionReference("nodeSpec"))),
                                OperationSchema("removeNode", ("target", SelectorSchema())),
                                OperationSchema("moveNode", ("target", SelectorSchema()), ("destination", SelectorSchema())),
                                OperationSchema("updateAttribute", ("target", SelectorSchema()), ("attribute", StringSchema()), ("value", StringSchema())))
                        }
                    })))
            );

            schema["$defs"] = new JObject
            {
                ["nodeSpec"] = StrictObject(
                    ("element", StringSchema()),
                    ("attributes", new JObject
                    {
                        ["type"] = "array",
                        ["items"] = StrictObject(
                            ("name", StringSchema()),
                            ("value", StringSchema()))
                    }),
                    ("children", new JObject
                    {
                        ["type"] = "array",
                        ["items"] = DefinitionReference("nodeSpec")
                    }))
            };
            return schema;
        }

        private static JObject OperationSchema(string operationType, params (string Name, JToken Schema)[] fields)
        {
            var allFields = new List<(string Name, JToken Schema)>
            {
                ("type", new JObject
                {
                    ["type"] = "string",
                    ["const"] = operationType
                })
            };
            allFields.AddRange(fields);
            return StrictObject(allFields.ToArray());
        }

        private static JObject SelectorSchema()
        {
            return new JObject
            {
                ["anyOf"] = new JArray(
                    StrictObject(("id", new JObject { ["type"] = "integer" })),
                    StrictObject(("path", StringSchema())))
            };
        }

        private static JObject StrictObject(params (string Name, JToken Schema)[] fields)
        {
            var properties = new JObject();
            foreach (var field in fields)
            {
                properties[field.Name] = field.Schema;
            }

            return new JObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JArray(fields.Select(field => field.Name)),
                ["additionalProperties"] = false
            };
        }

        private static JObject StringSchema()
        {
            return new JObject { ["type"] = "string" };
        }

        private static JObject NullableStringSchema()
        {
            return new JObject
            {
                ["anyOf"] = new JArray(
                    StringSchema(),
                    new JObject { ["type"] = "null" })
            };
        }

        private static JObject DefinitionReference(string name)
        {
            return new JObject { ["$ref"] = "#/$defs/" + name };
        }

        private static string BuildInitialInput(string prompt, string context)
        {
            return "USER REQUEST\n" + prompt + "\n\n" + context;
        }

        private static string BuildRepairInput(string invalidOutput, string validationError)
        {
            return "The previous model output was invalid. Return only a corrected JSON patch envelope.\n" +
                "Validation error: " + validationError + "\nInvalid output:\n" + invalidOutput;
        }

        private static bool TryParseEnvelope(
            string modelOutput,
            AiResponseMetadata metadata,
            out AiResponse response,
            out string validationError)
        {
            response = null!;
            try
            {
                response = ParseEnvelope(modelOutput, metadata);
                validationError = string.Empty;
                return true;
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is PatchApplyException ||
                exception is ArgumentException ||
                exception is FormatException)
            {
                validationError = exception.Message;
                return false;
            }
        }

        private static ModelExtraction ExtractModelResponse(
            ApiMode endpointMode,
            string responseBody,
            string apiKey)
        {
            try
            {
                var root = ParseJsonObject(responseBody, "API response");
                if (endpointMode == ApiMode.Responses)
                {
                    var metadata = ExtractResponsesMetadata(root);
                    if (root["message"]?.Type == JTokenType.String && root["patch"] is JObject)
                    {
                        return ModelExtraction.Output(responseBody, metadata);
                    }

                    if (root["output_text"]?.Type == JTokenType.String)
                    {
                        return ModelExtraction.Output(root["output_text"]!.Value<string>()!, metadata);
                    }

                    if (!(root["output"] is JArray output))
                    {
                        throw new JsonSerializationException("Responses output must be an array.");
                    }

                    foreach (var item in output.OfType<JObject>())
                    {
                        if (!string.Equals(item["type"]?.Value<string>(), "message", StringComparison.Ordinal) ||
                            !string.Equals(item["role"]?.Value<string>(), "assistant", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (!(item["content"] is JArray content))
                        {
                            throw new JsonSerializationException("Responses assistant message content must be an array.");
                        }

                        var refusal = content.OfType<JObject>().FirstOrDefault(candidate =>
                            string.Equals(candidate["type"]?.Value<string>(), "refusal", StringComparison.Ordinal) &&
                            candidate["refusal"]?.Type == JTokenType.String);
                        if (refusal != null)
                        {
                            return ModelExtraction.Refused(refusal["refusal"]!.Value<string>()!, metadata);
                        }

                        var outputText = content.OfType<JObject>().FirstOrDefault(candidate =>
                            string.Equals(candidate["type"]?.Value<string>(), "output_text", StringComparison.Ordinal) &&
                            candidate["text"]?.Type == JTokenType.String);
                        if (outputText != null)
                        {
                            return ModelExtraction.Output(outputText["text"]!.Value<string>()!, metadata);
                        }
                    }

                    throw new JsonSerializationException(
                        "Responses output does not contain an assistant message with output_text or refusal content.");
                }

                if (!(root["choices"] is JArray choices) ||
                    !(choices.FirstOrDefault() is JObject choice) ||
                    !(choice["message"] is JObject message) ||
                    !string.Equals(message["role"]?.Value<string>(), "assistant", StringComparison.Ordinal))
                {
                    throw new JsonSerializationException(
                        "Chat Completions output must contain choices[0].message with the assistant role.");
                }

                var chatMetadata = ExtractChatMetadata(root, message);
                if (message["refusal"]?.Type == JTokenType.String)
                {
                    return ModelExtraction.Refused(message["refusal"]!.Value<string>()!, chatMetadata);
                }

                if (message["content"]?.Type != JTokenType.String)
                {
                    throw new JsonSerializationException(
                        "Chat Completions assistant message must contain string content or refusal.");
                }

                return ModelExtraction.Output(message["content"]!.Value<string>()!, chatMetadata);
            }
            catch (Exception exception) when (
                exception is JsonException ||
                exception is ArgumentException ||
                exception is FormatException)
            {
                throw new InvalidOperationException(
                    "Invalid OpenAI-compatible response wrapper: " +
                    MakeDisplaySafe(exception.Message + " Body: " + responseBody, apiKey));
            }
        }

        private static AiResponse ParseEnvelope(string modelOutput, AiResponseMetadata metadata)
        {
            var root = ParseJsonObject(modelOutput, "patch envelope");
            var allowed = new HashSet<string>(new[] { "message", "patch" }, StringComparer.Ordinal);
            var unexpected = root.Properties().FirstOrDefault(property => !allowed.Contains(property.Name));
            if (unexpected != null)
            {
                throw new JsonSerializationException("Unknown patch envelope member '" + unexpected.Name + "'.");
            }

            if (root["message"]?.Type != JTokenType.String)
            {
                throw new JsonSerializationException("Patch envelope message must be a string.");
            }

            if (!(root["patch"] is JObject patchObject))
            {
                throw new JsonSerializationException("Patch envelope patch must be an object.");
            }

            var baseHash = patchObject["baseHash"]?.Value<string>();
            if (baseHash == null || baseHash.Length != 64 || baseHash.Any(character =>
                !((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f'))))
            {
                throw new JsonSerializationException("Patch baseHash must be a lowercase SHA-256 value.");
            }

            var normalizedPatch = NormalizePatchForDomain(patchObject);
            var patch = PatchDocument.Deserialize(normalizedPatch.ToString(Formatting.None));
            return new AiResponse(
                root["message"]!.Value<string>()!,
                patch,
                canApply: true,
                Array.Empty<string>(),
                metadata);
        }

        private static AiResponseMetadata ExtractResponsesMetadata(JObject root)
        {
            var summaries = root["output"] is JArray output
                ? output
                    .OfType<JObject>()
                    .Where(item =>
                        string.Equals(item["type"]?.Value<string>(), "reasoning", StringComparison.Ordinal))
                    .SelectMany(item => item["summary"] is JArray summary
                        ? summary.OfType<JObject>()
                        : Enumerable.Empty<JObject>())
                    .Where(item =>
                        string.Equals(item["type"]?.Value<string>(), "summary_text", StringComparison.Ordinal) &&
                        item["text"]?.Type == JTokenType.String)
                    .Select(item => item["text"]!.Value<string>()!)
                    .Where(text => !string.IsNullOrWhiteSpace(text))
                    .ToArray()
                : Array.Empty<string>();
            var usage = root["usage"] as JObject;
            return new AiResponseMetadata(
                summaries.Length == 0 ? null : string.Join("\n", summaries),
                ReadNonNegativeInteger(usage?["input_tokens"]),
                ReadNonNegativeInteger(usage?["output_tokens"]),
                wasSchemaRepair: false);
        }

        private static AiResponseMetadata ExtractChatMetadata(JObject root, JObject message)
        {
            var reasoningSummary = message["reasoning_summary"]?.Type == JTokenType.String
                ? message["reasoning_summary"]!.Value<string>()
                : null;
            var usage = root["usage"] as JObject;
            return new AiResponseMetadata(
                reasoningSummary,
                ReadNonNegativeInteger(usage?["prompt_tokens"]),
                ReadNonNegativeInteger(usage?["completion_tokens"]),
                wasSchemaRepair: false);
        }

        private static int? ReadNonNegativeInteger(JToken? token)
        {
            if (token?.Type != JTokenType.Integer ||
                !int.TryParse(
                    token.ToString(Formatting.None),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var value) ||
                value < 0)
            {
                return null;
            }

            return value;
        }

        private static AiResponseMetadata WithSchemaRepair(AiResponseMetadata metadata)
        {
            return new AiResponseMetadata(
                metadata.ReasoningSummary,
                metadata.InputTokens,
                metadata.OutputTokens,
                wasSchemaRepair: true);
        }

        private static JObject NormalizePatchForDomain(JObject patchObject)
        {
            var normalized = (JObject)patchObject.DeepClone();
            if (!(normalized["operations"] is JArray operations))
            {
                return normalized;
            }

            foreach (var operation in operations.OfType<JObject>())
            {
                if (string.Equals(operation["type"]?.Value<string>(), "addVariable", StringComparison.Ordinal) &&
                    operation["value"]?.Type == JTokenType.Null)
                {
                    operation.Remove("value");
                }

                if (operation["node"] is JObject node)
                {
                    NormalizeNodeSpecForDomain(node);
                }
            }

            return normalized;
        }

        private static void NormalizeNodeSpecForDomain(JObject node)
        {
            if (node["attributes"] is JArray wireAttributes)
            {
                var domainAttributes = new JObject();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var token in wireAttributes)
                {
                    if (!(token is JObject attribute) ||
                        attribute.Properties().Any(property =>
                            !string.Equals(property.Name, "name", StringComparison.Ordinal) &&
                            !string.Equals(property.Name, "value", StringComparison.Ordinal)) ||
                        attribute["name"]?.Type != JTokenType.String ||
                        attribute["value"]?.Type != JTokenType.String)
                    {
                        throw new JsonSerializationException(
                            "Each node attribute entry must contain exactly string fields 'name' and 'value'.");
                    }

                    var name = attribute["name"]!.Value<string>()!;
                    if (!names.Add(name))
                    {
                        throw new JsonSerializationException(
                            "Duplicate node attribute name '" + name + "'.");
                    }

                    domainAttributes[name] = attribute["value"]!.Value<string>();
                }

                node["attributes"] = domainAttributes;
            }

            if (node["children"] is JArray children)
            {
                foreach (var child in children.OfType<JObject>())
                {
                    NormalizeNodeSpecForDomain(child);
                }
            }
        }

        private sealed class ModelExtraction
        {
            private ModelExtraction(
                string? modelOutput,
                string? refusal,
                AiResponseMetadata metadata)
            {
                ModelOutput = modelOutput;
                Refusal = refusal;
                Metadata = metadata;
            }

            public string? ModelOutput { get; }

            public string? Refusal { get; }

            public AiResponseMetadata Metadata { get; }

            public static ModelExtraction Output(string value, AiResponseMetadata metadata)
            {
                return new ModelExtraction(value, null, metadata);
            }

            public static ModelExtraction Refused(string value, AiResponseMetadata metadata)
            {
                return new ModelExtraction(null, value, metadata);
            }
        }

        private static JObject ParseJsonObject(string json, string context)
        {
            using (var stringReader = new StringReader(json))
            using (var jsonReader = new JsonTextReader(stringReader))
            {
                jsonReader.DateParseHandling = DateParseHandling.None;
                var root = JObject.Load(jsonReader, new JsonLoadSettings
                {
                    CommentHandling = CommentHandling.Ignore,
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    LineInfoHandling = LineInfoHandling.Load
                });
                if (jsonReader.Read())
                {
                    throw new JsonSerializationException(context + " must contain exactly one JSON object.");
                }

                return root;
            }
        }

        private static void EnsureSuccess(HttpTransportResponse response, string apiKey)
        {
            if (response.StatusCode >= 200 && response.StatusCode <= 299)
            {
                return;
            }

            var body = MakeDisplaySafe(DecodeBody(response), apiKey);
            throw new OpenAiApiException(
                response.StatusCode,
                "OpenAI-compatible API request failed with HTTP " +
                response.StatusCode.ToString(CultureInfo.InvariantCulture) + ": " + body);
        }

        private static bool IsResponsesEndpointUnavailable(int statusCode, string body)
        {
            if (statusCode != 404 && statusCode != 405)
            {
                return false;
            }

            try
            {
                var root = JObject.Parse(body);
                var code = root["error"] is JObject error ? error["code"] : null;
                if (code?.Type == JTokenType.String)
                {
                    var codeValue = code.Value<string>();
                    return string.Equals(codeValue, "endpoint_not_found", StringComparison.Ordinal) ||
                        string.Equals(codeValue, "endpoint_not_supported", StringComparison.Ordinal);
                }
            }
            catch (JsonException)
            {
            }

            var evidence = body.ToLowerInvariant();
            return ContainsAny(evidence,
                "responses endpoint not found",
                "responses endpoint is not found",
                "responses endpoint not supported",
                "responses endpoint is not supported",
                "responses endpoint unsupported",
                "responses endpoint is unsupported",
                "responses endpoint unavailable",
                "responses endpoint is unavailable",
                "responses endpoint does not exist",
                "cannot post /v1/responses",
                "/v1/responses not found",
                "/v1/responses is not found",
                "/v1/responses not supported",
                "/v1/responses is not supported",
                "/v1/responses unsupported",
                "/v1/responses unavailable",
                "/v1/responses does not exist");
        }

        private static bool ContainsAny(string value, params string[] phrases)
        {
            return phrases.Any(phrase => value.Contains(phrase));
        }

        private static string DecodeBody(HttpTransportResponse response)
        {
            return Encoding.UTF8.GetString(response.Body);
        }

        private static string MakeDisplaySafe(string value, string apiKey)
        {
            var redacted = SecretRedactor.Redact(value, apiKey)
                .Replace("\r\n", "\n")
                .Replace('\r', '\n');
            var builder = new StringBuilder(Math.Min(redacted.Length, MaximumDiagnosticLength));
            foreach (var character in redacted)
            {
                builder.Append(char.IsControl(character) ? ' ' : character);
                if (builder.Length == MaximumDiagnosticLength)
                {
                    break;
                }
            }

            if (builder.Length > 0 && char.IsHighSurrogate(builder[builder.Length - 1]))
            {
                builder.Length--;
            }

            return builder.ToString();
        }
    }
}
