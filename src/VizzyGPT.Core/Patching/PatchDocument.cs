using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
                var normalizedJson = ValidateJsonProtocolSyntax(json);

                JObject root;
                using (var stringReader = new StringReader(normalizedJson))
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

        private static string ValidateJsonProtocolSyntax(string json)
        {
            return new JsonSyntaxValidator(json).Validate();
        }

        private sealed class JsonSyntaxValidator
        {
            private const int MaximumNestingDepth = 64;

            private readonly string json;
            private readonly List<NumberReplacement> numberReplacements = new List<NumberReplacement>();
            private int index;

            public JsonSyntaxValidator(string json)
            {
                this.json = json;
            }

            public string Validate()
            {
                SkipWhitespace();
                ParseValue(0);
                SkipWhitespace();
                if (index != json.Length)
                {
                    ThrowSyntaxError("JSON contains trailing content.");
                }

                if (numberReplacements.Count == 0)
                {
                    return json;
                }

                var normalized = new StringBuilder(json.Length);
                var sourceIndex = 0;
                foreach (var replacement in numberReplacements)
                {
                    normalized.Append(json, sourceIndex, replacement.Start - sourceIndex);
                    normalized.Append(replacement.Value);
                    sourceIndex = replacement.Start + replacement.Length;
                }

                normalized.Append(json, sourceIndex, json.Length - sourceIndex);
                return normalized.ToString();
            }

            private void ParseValue(int containerDepth)
            {
                if (index >= json.Length)
                {
                    ThrowSyntaxError("JSON value is missing.");
                }

                switch (json[index])
                {
                    case '{':
                        ParseObject(EnterContainer(containerDepth));
                        return;
                    case '[':
                        ParseArray(EnterContainer(containerDepth));
                        return;
                    case '"':
                        ParseString();
                        return;
                    case 't':
                        ParseLiteral("true");
                        return;
                    case 'f':
                        ParseLiteral("false");
                        return;
                    case 'n':
                        ParseLiteral("null");
                        return;
                    case '-':
                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        ParseNumber();
                        return;
                    default:
                        ThrowSyntaxError("Invalid JSON value.");
                        return;
                }
            }

            private void ParseObject(int containerDepth)
            {
                Expect('{');
                SkipWhitespace();
                if (TryConsume('}'))
                {
                    return;
                }

                while (true)
                {
                    if (index >= json.Length || json[index] != '"')
                    {
                        ThrowSyntaxError("JSON object member names must be double-quoted strings.");
                    }

                    ParseString();
                    SkipWhitespace();
                    Expect(':');
                    SkipWhitespace();
                    ParseValue(containerDepth);
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return;
                    }

                    Expect(',');
                    SkipWhitespace();
                }
            }

            private void ParseArray(int containerDepth)
            {
                Expect('[');
                SkipWhitespace();
                if (TryConsume(']'))
                {
                    return;
                }

                while (true)
                {
                    ParseValue(containerDepth);
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return;
                    }

                    Expect(',');
                    SkipWhitespace();
                }
            }

            private int EnterContainer(int containerDepth)
            {
                var nextDepth = containerDepth + 1;
                if (nextDepth > MaximumNestingDepth)
                {
                    ThrowSyntaxError("JSON nesting depth exceeds protocol maximum of 64");
                }

                return nextDepth;
            }

            private void ParseString()
            {
                Expect('"');
                while (index < json.Length)
                {
                    var current = json[index++];
                    if (current == '"')
                    {
                        return;
                    }

                    if (current < '\u0020')
                    {
                        ThrowSyntaxError("JSON strings cannot contain unescaped control characters.");
                    }

                    if (current != '\\')
                    {
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        ThrowSyntaxError("JSON string escape is incomplete.");
                    }

                    var escape = json[index++];
                    switch (escape)
                    {
                        case '"':
                        case '\\':
                        case '/':
                        case 'b':
                        case 'f':
                        case 'n':
                        case 'r':
                        case 't':
                            break;
                        case 'u':
                            for (var digit = 0; digit < 4; digit++)
                            {
                                if (index >= json.Length || !IsHexDigit(json[index]))
                                {
                                    ThrowSyntaxError("JSON Unicode escapes require exactly four hexadecimal digits.");
                                }

                                index++;
                            }

                            break;
                        default:
                            ThrowSyntaxError("Invalid JSON string escape.");
                            break;
                    }
                }

                ThrowSyntaxError("JSON string is unterminated.");
            }

            private void ParseNumber()
            {
                var start = index;
                if (TryConsume('-') && index >= json.Length)
                {
                    ThrowSyntaxError("JSON number is incomplete.");
                }

                if (TryConsume('0'))
                {
                    if (index < json.Length && IsDigit(json[index]))
                    {
                        ThrowSyntaxError("JSON numbers cannot contain leading zeroes.");
                    }
                }
                else
                {
                    if (index >= json.Length || json[index] < '1' || json[index] > '9')
                    {
                        ThrowSyntaxError("JSON number requires an integer part.");
                    }

                    while (index < json.Length && IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                if (TryConsume('.'))
                {
                    RequireDigit("JSON number fractions require at least one digit.");
                    while (index < json.Length && IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                if (index < json.Length && (json[index] == 'e' || json[index] == 'E'))
                {
                    index++;
                    if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    {
                        index++;
                    }

                    RequireDigit("JSON number exponents require at least one digit.");
                    while (index < json.Length && IsDigit(json[index]))
                    {
                        index++;
                    }
                }

                var length = index - start;
                if (TryNormalizeInt32(json, start, length, out var normalized))
                {
                    var original = json.Substring(start, length);
                    if (!string.Equals(original, normalized, StringComparison.Ordinal))
                    {
                        numberReplacements.Add(new NumberReplacement(start, length, normalized));
                    }
                }
            }

            private void ParseLiteral(string literal)
            {
                foreach (var expected in literal)
                {
                    if (index >= json.Length || json[index] != expected)
                    {
                        ThrowSyntaxError("Invalid JSON literal.");
                    }

                    index++;
                }
            }

            private void SkipWhitespace()
            {
                while (index < json.Length)
                {
                    var current = json[index];
                    if (current != '\u0020' && current != '\t' && current != '\r' && current != '\n')
                    {
                        return;
                    }

                    index++;
                }
            }

            private void RequireDigit(string message)
            {
                if (index >= json.Length || !IsDigit(json[index]))
                {
                    ThrowSyntaxError(message);
                }
            }

            private void Expect(char expected)
            {
                if (!TryConsume(expected))
                {
                    ThrowSyntaxError("Expected '" + expected + "'.");
                }
            }

            private bool TryConsume(char expected)
            {
                if (index >= json.Length || json[index] != expected)
                {
                    return false;
                }

                index++;
                return true;
            }

            private void ThrowSyntaxError(string message)
            {
                throw new JsonSerializationException(message + " Position " + index.ToString(CultureInfo.InvariantCulture) + ".");
            }

            private static bool IsDigit(char value)
            {
                return value >= '0' && value <= '9';
            }

            private static bool IsHexDigit(char value)
            {
                return IsDigit(value) ||
                    (value >= 'a' && value <= 'f') ||
                    (value >= 'A' && value <= 'F');
            }

            private static bool TryNormalizeInt32(string value, int start, int length, out string normalized)
            {
                var end = start + length;
                var cursor = start;
                var negative = value[cursor] == '-';
                if (negative)
                {
                    cursor++;
                }

                var integerStart = cursor;
                while (cursor < end && IsDigit(value[cursor]))
                {
                    cursor++;
                }

                var integerLength = cursor - integerStart;
                var fractionStart = cursor;
                var fractionLength = 0;
                if (cursor < end && value[cursor] == '.')
                {
                    cursor++;
                    fractionStart = cursor;
                    while (cursor < end && IsDigit(value[cursor]))
                    {
                        cursor++;
                    }

                    fractionLength = cursor - fractionStart;
                }

                long exponent = 0;
                if (cursor < end && (value[cursor] == 'e' || value[cursor] == 'E'))
                {
                    cursor++;
                    var exponentNegative = false;
                    if (cursor < end && (value[cursor] == '+' || value[cursor] == '-'))
                    {
                        exponentNegative = value[cursor] == '-';
                        cursor++;
                    }

                    var exponentLimit = (long)length + 20;
                    while (cursor < end)
                    {
                        var digit = value[cursor++] - '0';
                        exponent = exponent > exponentLimit
                            ? exponentLimit
                            : Math.Min(exponentLimit, (exponent * 10) + digit);
                    }

                    if (exponentNegative)
                    {
                        exponent = -exponent;
                    }
                }

                var combinedLength = integerLength + fractionLength;
                var firstNonZero = -1;
                for (var digitIndex = 0; digitIndex < combinedLength; digitIndex++)
                {
                    if (CombinedDigit(value, integerStart, integerLength, fractionStart, digitIndex) != '0')
                    {
                        firstNonZero = digitIndex;
                        break;
                    }
                }

                if (firstNonZero < 0)
                {
                    normalized = "0";
                    return true;
                }

                var decimalPosition = integerLength + exponent;
                if (decimalPosition <= 0)
                {
                    normalized = string.Empty;
                    return false;
                }

                if (decimalPosition < combinedLength)
                {
                    for (var digitIndex = (int)decimalPosition; digitIndex < combinedLength; digitIndex++)
                    {
                        if (CombinedDigit(value, integerStart, integerLength, fractionStart, digitIndex) != '0')
                        {
                            normalized = string.Empty;
                            return false;
                        }
                    }
                }

                var significantLength = decimalPosition - firstNonZero;
                if (significantLength > 10)
                {
                    normalized = string.Empty;
                    return false;
                }

                long magnitude = 0;
                for (long digitIndex = firstNonZero; digitIndex < decimalPosition; digitIndex++)
                {
                    var digit = digitIndex < combinedLength
                        ? CombinedDigit(value, integerStart, integerLength, fractionStart, (int)digitIndex) - '0'
                        : 0;
                    magnitude = (magnitude * 10) + digit;
                }

                var maximum = negative ? 2147483648L : int.MaxValue;
                if (magnitude > maximum)
                {
                    normalized = string.Empty;
                    return false;
                }

                var result = negative ? -magnitude : magnitude;
                normalized = result.ToString(CultureInfo.InvariantCulture);
                return true;
            }

            private static char CombinedDigit(
                string value,
                int integerStart,
                int integerLength,
                int fractionStart,
                int digitIndex)
            {
                return digitIndex < integerLength
                    ? value[integerStart + digitIndex]
                    : value[fractionStart + digitIndex - integerLength];
            }

            private readonly struct NumberReplacement
            {
                public NumberReplacement(int start, int length, string value)
                {
                    Start = start;
                    Length = length;
                    Value = value;
                }

                public int Start { get; }

                public int Length { get; }

                public string Value { get; }
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
                if (id.Type != JTokenType.Integer ||
                    !int.TryParse(id.ToString(Formatting.None), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var normalizedId))
                {
                    throw new JsonSerializationException("Node selector id must be an Int32 integer.");
                }

                selector["id"] = normalizedId;
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
