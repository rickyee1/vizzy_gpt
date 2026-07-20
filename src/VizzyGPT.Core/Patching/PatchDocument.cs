using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VizzyGPT.Core.Patching
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PatchDocument
    {
        [JsonConstructor]
        public PatchDocument(string baseHash, string summary, IReadOnlyList<PatchOperation> operations)
        {
            if (string.IsNullOrEmpty(baseHash))
            {
                throw new PatchApplyException("Patch baseHash must be a non-empty string.");
            }

            if (string.IsNullOrWhiteSpace(summary))
            {
                throw new PatchApplyException("Patch summary must be a non-whitespace string.");
            }

            if (operations == null || operations.Count == 0)
            {
                throw new PatchApplyException("Patch operations must contain at least one operation.");
            }

            if (operations.Any(operation => operation == null))
            {
                throw new PatchApplyException("Patch operations cannot contain null entries.");
            }

            BaseHash = baseHash;
            Summary = summary;
            Operations = new ReadOnlyCollection<PatchOperation>(operations.ToArray());
        }

        [JsonProperty("baseHash", Required = Required.Always)]
        public string BaseHash { get; }

        [JsonProperty("summary", Required = Required.Always)]
        public string Summary { get; }

        [JsonProperty("operations", Required = Required.Always)]
        public IReadOnlyList<PatchOperation> Operations { get; }

        public static PatchDocument Deserialize(string json)
        {
            if (json == null)
            {
                throw new PatchApplyException("Patch JSON cannot be null.");
            }

            try
            {
                ValidateJsonProtocolSyntax(json);

                JObject root;
                using (var stringReader = new StringReader(json))
                using (var jsonReader = new JsonTextReader(stringReader))
                {
                    jsonReader.DateParseHandling = DateParseHandling.None;

                    root = JObject.Load(
                        jsonReader,
                        new JsonLoadSettings
                        {
                            CommentHandling = CommentHandling.Ignore,
                            DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                            LineInfoHandling = LineInfoHandling.Load
                        });

                    if (jsonReader.Read())
                    {
                        throw new JsonSerializationException("Patch JSON must contain exactly one object.");
                    }
                }

                ValidateJsonContract(root);

                var serializer = JsonSerializer.Create(
                    new JsonSerializerSettings
                    {
                        MissingMemberHandling = MissingMemberHandling.Error
                    });
                var document = root.ToObject<PatchDocument>(serializer);
                return document ?? throw new PatchApplyException("Patch JSON deserialized to null.");
            }
            catch (PatchApplyException)
            {
                throw;
            }
            catch (JsonException exception)
            {
                throw new PatchApplyException("Invalid patch JSON: " + exception.Message, exception);
            }
            catch (ArgumentException exception)
            {
                throw new PatchApplyException("Invalid patch contract: " + exception.Message, exception);
            }
        }

        private static void ValidateJsonProtocolSyntax(string json)
        {
            var inString = false;
            var escaped = false;

            for (var index = 0; index < json.Length; index++)
            {
                var current = json[index];
                if (inString)
                {
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (current == '\\')
                    {
                        escaped = true;
                    }
                    else if (current == '"')
                    {
                        inString = false;
                    }

                    continue;
                }

                if (current == '"')
                {
                    inString = true;
                    continue;
                }

                if (current == '/' && index + 1 < json.Length &&
                    (json[index + 1] == '/' || json[index + 1] == '*'))
                {
                    throw new JsonSerializationException("Patch JSON comments are not permitted.");
                }

                if (current == ',')
                {
                    var next = index + 1;
                    while (next < json.Length && char.IsWhiteSpace(json[next]))
                    {
                        next++;
                    }

                    if (next < json.Length && (json[next] == '}' || json[next] == ']'))
                    {
                        throw new JsonSerializationException("Patch JSON trailing commas are not permitted.");
                    }
                }
            }
        }

        private static void ValidateJsonContract(JObject root)
        {
            RequireOnlyProperties(root, "patch document", "baseHash", "summary", "operations");
            RequireString(root, "baseHash", "patch document");
            RequireString(root, "summary", "patch document");

            var operations = RequireArray(root, "operations", "patch document");
            foreach (var token in operations)
            {
                if (!(token is JObject operation))
                {
                    throw new JsonSerializationException("Each patch operation must be an object.");
                }

                ValidateOperation(operation);
            }
        }

        private static void ValidateOperation(JObject operation)
        {
            var type = RequireString(operation, "type", "patch operation");
            string[] required;
            string[] optional;
            switch (type)
            {
                case "addVariable":
                    required = new[] { "name" };
                    optional = new[] { "value" };
                    break;
                case "renameVariable":
                    required = new[] { "name", "newName" };
                    optional = Array.Empty<string>();
                    break;
                case "removeVariable":
                    required = new[] { "name" };
                    optional = Array.Empty<string>();
                    break;
                case "insertBefore":
                case "insertAfter":
                case "insertChild":
                case "replaceNode":
                    required = new[] { "target", "node" };
                    optional = Array.Empty<string>();
                    break;
                case "removeNode":
                    required = new[] { "target" };
                    optional = Array.Empty<string>();
                    break;
                case "moveNode":
                    required = new[] { "target", "destination" };
                    optional = Array.Empty<string>();
                    break;
                case "updateAttribute":
                    required = new[] { "target", "attribute", "value" };
                    optional = Array.Empty<string>();
                    break;
                default:
                    throw new JsonSerializationException("Unknown patch operation type '" + type + "'.");
            }

            RequireOnlyProperties(operation, "patch operation '" + type + "'", new[] { "type" }.Concat(required).Concat(optional).ToArray());
            foreach (var field in required)
            {
                RequirePresent(operation, field, "patch operation '" + type + "'");
            }

            foreach (var property in operation.Properties().Where(property => !string.Equals(property.Name, "type", StringComparison.Ordinal)))
            {
                switch (property.Name)
                {
                    case "target":
                    case "destination":
                        ValidateSelector(RequireObject(property.Value, property.Name, "patch operation '" + type + "'"));
                        break;
                    case "node":
                        ValidateNodeSpec(RequireObject(property.Value, property.Name, "patch operation '" + type + "'"));
                        break;
                    default:
                        RequireString(operation, property.Name, "patch operation '" + type + "'");
                        break;
                }
            }
        }

        private static void ValidateSelector(JObject selector)
        {
            RequireOnlyProperties(selector, "node selector", "id", "path");
            if (selector.TryGetValue("id", StringComparison.Ordinal, out var id))
            {
                if (id.Type != JTokenType.Integer)
                {
                    throw new JsonSerializationException("Node selector id must be an integer.");
                }
            }

            if (selector.TryGetValue("path", StringComparison.Ordinal, out _))
            {
                RequireString(selector, "path", "node selector");
            }
        }

        private static void ValidateNodeSpec(JObject node)
        {
            RequireOnlyProperties(node, "node specification", "element", "attributes", "children");
            RequireString(node, "element", "node specification");

            var attributes = RequireObject(node, "attributes", "node specification");
            foreach (var attribute in attributes.Properties())
            {
                if (attribute.Value.Type != JTokenType.String)
                {
                    throw new JsonSerializationException("Node attribute '" + attribute.Name + "' must be a string.");
                }
            }

            var children = RequireArray(node, "children", "node specification");
            foreach (var child in children)
            {
                ValidateNodeSpec(RequireObject(child, "children", "node specification"));
            }
        }

        private static void RequireOnlyProperties(JObject value, string context, params string[] allowedNames)
        {
            var allowed = new HashSet<string>(allowedNames, StringComparer.Ordinal);
            var unexpected = value.Properties().FirstOrDefault(property => !allowed.Contains(property.Name));
            if (unexpected != null)
            {
                throw new JsonSerializationException("Unknown member '" + unexpected.Name + "' on " + context + ".");
            }
        }

        private static JToken RequirePresent(JObject value, string propertyName, string context)
        {
            if (!value.TryGetValue(propertyName, StringComparison.Ordinal, out var property) || property.Type == JTokenType.Null)
            {
                throw new JsonSerializationException("Required member '" + propertyName + "' is missing or null on " + context + ".");
            }

            return property;
        }

        private static string RequireString(JObject value, string propertyName, string context)
        {
            var property = RequirePresent(value, propertyName, context);
            if (property.Type != JTokenType.String)
            {
                throw new JsonSerializationException("Member '" + propertyName + "' on " + context + " must be a string.");
            }

            return property.Value<string>()!;
        }

        private static JObject RequireObject(JObject value, string propertyName, string context)
        {
            return RequireObject(RequirePresent(value, propertyName, context), propertyName, context);
        }

        private static JObject RequireObject(JToken value, string propertyName, string context)
        {
            return value as JObject
                ?? throw new JsonSerializationException("Member '" + propertyName + "' on " + context + " must be an object.");
        }

        private static JArray RequireArray(JObject value, string propertyName, string context)
        {
            return RequirePresent(value, propertyName, context) as JArray
                ?? throw new JsonSerializationException("Member '" + propertyName + "' on " + context + " must be an array.");
        }
    }
}
