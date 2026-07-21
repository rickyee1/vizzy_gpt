using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace VizzyGPT.Core.Security
{
    public static class SecretRedactor
    {
        private const string Placeholder = "[REDACTED]";

        private static readonly Regex BearerHeader = new Regex(
            @"(authorization\s*:\s*bearer\s+)([^\s\""'<>;,]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex EntityApiKey = new Regex(
            @"(?<prefix>&quot;api_key&quot;\s*:\s*&quot;)(?<secret>(?:(?!&quot;).)*)(?=&quot;)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.Singleline);

        public static string Redact(string text, string? configuredApiKey = null)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }

            var redacted = string.IsNullOrEmpty(configuredApiKey)
                ? text
                : ReplaceOrdinal(text, configuredApiKey, Placeholder);
            return RedactStructuredValues(redacted, 0);
        }

        private static string RedactStructuredValues(string text, int depth)
        {
            if (depth > 8)
            {
                return Placeholder;
            }

            var redacted = EntityApiKey.Replace(
                text,
                match => match.Groups["prefix"].Value + Placeholder);
            redacted = RedactJsonApiKeyValues(redacted, depth);
            return BearerHeader.Replace(redacted, match => match.Groups[1].Value + Placeholder);
        }

        private static string RedactJsonApiKeyValues(string text, int depth)
        {
            var replacements = new List<Replacement>();
            for (var index = 0; index < text.Length; index++)
            {
                if (text[index] != '"' || !TryReadJsonString(text, index, out var propertyEnd, out var propertyName))
                {
                    continue;
                }

                var cursor = propertyEnd;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                {
                    cursor++;
                }

                if (!string.Equals(propertyName, "api_key", StringComparison.Ordinal) ||
                    cursor >= text.Length || text[cursor] != ':')
                {
                    var nested = RedactStructuredValues(propertyName, depth + 1);
                    if (!string.Equals(nested, propertyName, StringComparison.Ordinal))
                    {
                        replacements.Add(new Replacement(
                            index,
                            propertyEnd - index,
                            JsonConvert.SerializeObject(nested)));
                    }

                    index = propertyEnd - 1;
                    continue;
                }

                cursor++;
                while (cursor < text.Length && char.IsWhiteSpace(text[cursor]))
                {
                    cursor++;
                }

                if (cursor >= text.Length || text[cursor] != '"')
                {
                    index = propertyEnd - 1;
                    continue;
                }

                if (!TryReadJsonString(text, cursor, out var valueEnd, out _))
                {
                    replacements.Add(new Replacement(
                        cursor,
                        text.Length - cursor,
                        '"' + Placeholder + '"'));
                    break;
                }

                replacements.Add(new Replacement(
                    cursor,
                    valueEnd - cursor,
                    '"' + Placeholder + '"'));
                index = valueEnd - 1;
            }

            if (replacements.Count == 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            var sourceIndex = 0;
            foreach (var replacement in replacements)
            {
                builder.Append(text, sourceIndex, replacement.Start - sourceIndex);
                builder.Append(replacement.Value);
                sourceIndex = replacement.Start + replacement.Length;
            }

            builder.Append(text, sourceIndex, text.Length - sourceIndex);
            return builder.ToString();
        }

        private static bool TryReadJsonString(string text, int start, out int end, out string value)
        {
            end = start;
            value = string.Empty;
            var escaped = false;
            for (var index = start + 1; index < text.Length; index++)
            {
                var current = text[index];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current != '"')
                {
                    continue;
                }

                end = index + 1;
                try
                {
                    value = JsonConvert.DeserializeObject<string>(text.Substring(start, end - start)) ?? string.Empty;
                    return true;
                }
                catch (JsonException)
                {
                    return false;
                }
            }

            return false;
        }

        private static string ReplaceOrdinal(string text, string oldValue, string newValue)
        {
            var first = text.IndexOf(oldValue, StringComparison.Ordinal);
            if (first < 0)
            {
                return text;
            }

            var builder = new StringBuilder(text.Length);
            var sourceIndex = 0;
            while (first >= 0)
            {
                builder.Append(text, sourceIndex, first - sourceIndex);
                builder.Append(newValue);
                sourceIndex = first + oldValue.Length;
                first = text.IndexOf(oldValue, sourceIndex, StringComparison.Ordinal);
            }

            builder.Append(text, sourceIndex, text.Length - sourceIndex);
            return builder.ToString();
        }

        private readonly struct Replacement
        {
            public Replacement(int start, int length, string value)
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
}
