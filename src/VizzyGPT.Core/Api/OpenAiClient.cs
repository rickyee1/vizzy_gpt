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
            if (TryParseModelResponse(endpointMode, firstBody, out var validResponse, out var invalidOutput, out var validationError))
            {
                return validResponse;
            }

            var repairInput = BuildRepairInput(invalidOutput, validationError);
            var repairResponse = await SendToEndpointAsync(request, endpointMode, repairInput, cancellationToken).ConfigureAwait(false);
            EnsureSuccess(repairResponse, request.ApiKey);

            var repairBody = DecodeBody(repairResponse);
            if (TryParseModelResponse(endpointMode, repairBody, out validResponse, out invalidOutput, out validationError))
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
                new[] { diagnostic });
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

            return await transport.SendAsync(transportRequest, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The AI transport returned a null response.");
        }

        private static JObject CreateResponsesPayload(string model, string input)
        {
            return new JObject
            {
                ["model"] = model,
                ["input"] = input,
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
                        ["content"] = "Return only a JSON object matching the supplied Vizzy patch envelope schema."
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
                            ["oneOf"] = new JArray(
                                OperationSchema("addVariable", ("name", StringSchema()), ("value", StringSchema())),
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
                    ("attributes", StrictObject()),
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
                ("type", new JObject { ["const"] = operationType })
            };
            allFields.AddRange(fields);
            return StrictObject(allFields.ToArray());
        }

        private static JObject SelectorSchema()
        {
            return new JObject
            {
                ["oneOf"] = new JArray(
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

        private static bool TryParseModelResponse(
            ApiMode endpointMode,
            string responseBody,
            out AiResponse response,
            out string invalidOutput,
            out string validationError)
        {
            response = null!;
            invalidOutput = responseBody;
            try
            {
                invalidOutput = ExtractModelOutput(endpointMode, responseBody);
                response = ParseEnvelope(invalidOutput);
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

        private static string ExtractModelOutput(ApiMode endpointMode, string responseBody)
        {
            var root = ParseJsonObject(responseBody, "API response");
            if (endpointMode == ApiMode.Responses)
            {
                if (root["message"]?.Type == JTokenType.String && root["patch"] is JObject)
                {
                    return responseBody;
                }

                if (root["output_text"]?.Type == JTokenType.String)
                {
                    return root["output_text"]!.Value<string>()!;
                }

                if (root["output"] is JArray output)
                {
                    foreach (var item in output.OfType<JObject>())
                    {
                        if (!(item["content"] is JArray content))
                        {
                            continue;
                        }

                        var outputText = content.OfType<JObject>().FirstOrDefault(candidate =>
                            string.Equals(candidate["type"]?.Value<string>(), "output_text", StringComparison.Ordinal) &&
                            candidate["text"]?.Type == JTokenType.String);
                        if (outputText != null)
                        {
                            return outputText["text"]!.Value<string>()!;
                        }
                    }
                }

                throw new JsonSerializationException("Responses output does not contain assistant output_text.");
            }

            var contentToken = root["choices"] is JArray choices && choices.FirstOrDefault() is JObject choice
                ? choice["message"]?["content"]
                : null;
            if (contentToken?.Type != JTokenType.String)
            {
                throw new JsonSerializationException("Chat Completions output does not contain choices[0].message.content.");
            }

            return contentToken.Value<string>()!;
        }

        private static AiResponse ParseEnvelope(string modelOutput)
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

            var patch = PatchDocument.Deserialize(patchObject.ToString(Formatting.None));
            return new AiResponse(
                root["message"]!.Value<string>()!,
                patch,
                canApply: true,
                Array.Empty<string>());
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

            var evidence = body.ToLowerInvariant();
            var identifiesEndpoint = evidence.Contains("responses endpoint") ||
                evidence.Contains("response endpoint") ||
                evidence.Contains("this endpoint") ||
                evidence.Contains("/v1/responses") ||
                evidence.Contains("endpoint_not_found") ||
                evidence.Contains("endpoint_not_supported") ||
                evidence.Contains("unsupported_endpoint");
            var unavailable = evidence.Contains("not found") ||
                evidence.Contains("not supported") ||
                evidence.Contains("unsupported") ||
                evidence.Contains("unavailable") ||
                evidence.Contains("does not exist") ||
                evidence.Contains("cannot post");
            return identifiesEndpoint && unavailable;
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
