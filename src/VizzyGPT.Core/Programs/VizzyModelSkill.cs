using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace VizzyGPT.Core.Programs
{
    public static class VizzyModelSkill
    {
        public const int MaximumReferenceCharacters = 24000;

        private const int MaximumRepairReferenceCharacters = 4000;
        private const string TruncationMarker = "[CATALOG TRUNCATED]";
        private const string ReferenceHeader =
            "VIZZY MODEL SKILL\n" +
            "- Events start cooperative instruction sequences.\n" +
            "- Instructions execute sequentially; expressions supply typed values.\n" +
            "- Persistent loops must yield with the catalog's wait instruction.\n" +
            "- Craft controls use SetInput; do not invent dedicated throttle/pitch/roll nodes.\n" +
            "- Program contains exactly one direct Variables and Expressions container, plus zero or more direct Instructions stacks.\n" +
            "- Preserve existing structural containers. If no direct Instructions stack exists, create one with insertChild targeting /Program[0]; never add or remove Variables or Expressions.\n" +
            "- Prefer id selectors from the current program XML. If a path is required, use an absolute canonical indexed path such as /Program[0]/Instructions[0]/Event[0]; every path segment must include [index].\n" +
            "- Use only listed element/style combinations and preserve each template's child shape.\n" +
            "\n" +
            "CURRENT VIZZY NODE TEMPLATES\n";

        public static string BuildModifyReference(VizzyNodeCatalog catalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            return BuildReference(catalog.Templates, MaximumReferenceCharacters);
        }

        public static string BuildRepairReference(
            VizzyNodeCatalog catalog,
            string code,
            string? path,
            string message)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var tokens = new HashSet<string>(Tokenize(code, path, message), StringComparer.OrdinalIgnoreCase);
            var matches = catalog.Templates
                .Where(template => tokens.Any(token => template.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToArray();

            return BuildReference(matches.Length == 0 ? catalog.Templates : matches, MaximumRepairReferenceCharacters);
        }

        private static IEnumerable<string> Tokenize(string code, string? path, string message)
        {
            foreach (var source in new[] { code, path, message })
            {
                if (string.IsNullOrEmpty(source))
                {
                    continue;
                }

                var start = 0;
                for (var index = 0; index <= source.Length; index++)
                {
                    if (index < source.Length && char.IsLetterOrDigit(source[index]))
                    {
                        continue;
                    }

                    if (index - start >= 4)
                    {
                        yield return source.Substring(start, index - start);
                    }

                    start = index + 1;
                }
            }
        }

        private static string BuildReference(IReadOnlyList<string> templates, int maximumCharacters)
        {
            var builder = new StringBuilder(ReferenceHeader);
            for (var index = 0; index < templates.Count; index++)
            {
                var templateLine = templates[index] + "\n";
                var needsTruncationMarker = index < templates.Count - 1;
                var markerLength = needsTruncationMarker ? TruncationMarker.Length : 0;
                if (builder.Length + templateLine.Length + markerLength > maximumCharacters)
                {
                    builder.Append(TruncationMarker);
                    break;
                }

                builder.Append(templateLine);
            }

            return builder.ToString();
        }
    }
}
