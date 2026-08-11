using System;
using System.Globalization;
using System.Xml;
using Newtonsoft.Json;

namespace VizzyGPT.Core.Patching
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NodeSelector
    {
        [JsonConstructor]
        public NodeSelector(int? id, string? path)
        {
            if (id.HasValue == (path != null))
            {
                throw new PatchApplyException("A node selector must define exactly one of id or path.");
            }

            if (path != null && !IsCanonicalPath(path))
            {
                throw new PatchApplyException("A node selector path must be an absolute canonical indexed path.");
            }

            Id = id;
            Path = path;
        }

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public int? Id { get; }

        [JsonProperty("path", NullValueHandling = NullValueHandling.Ignore)]
        public string? Path { get; }

        private static bool IsCanonicalPath(string path)
        {
            if (path.Length < 2 || path[0] != '/' || path[path.Length - 1] == '/' || path.StartsWith("//", StringComparison.Ordinal))
            {
                return false;
            }

            var segments = path.Substring(1).Split('/');
            if (!string.Equals(segments[0], "Program[0]", StringComparison.Ordinal))
            {
                return false;
            }

            foreach (var segment in segments)
            {
                var openBracket = segment.IndexOf('[');
                if (openBracket <= 0 || openBracket != segment.LastIndexOf('[') || segment.IndexOf(']', openBracket + 1) != segment.Length - 1)
                {
                    return false;
                }

                var name = segment.Substring(0, openBracket);
                try
                {
                    XmlConvert.VerifyNCName(name);
                }
                catch (XmlException)
                {
                    return false;
                }

                var indexText = segment.Substring(openBracket + 1, segment.Length - openBracket - 2);
                if (!int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out var index) || index < 0 || !string.Equals(index.ToString(CultureInfo.InvariantCulture), indexText, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
