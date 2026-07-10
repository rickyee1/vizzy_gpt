using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace VizzyGPT.Core.Programs
{
    public sealed class VizzyProgramDocument
    {
        private VizzyProgramDocument(XElement root)
        {
            Root = root;
        }

        public XElement Root { get; }

        public static VizzyProgramDocument Parse(string xml)
        {
            if (xml == null)
            {
                throw new ArgumentNullException(nameof(xml));
            }

            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            if (!string.Equals(root.Name.LocalName, "Program", StringComparison.Ordinal) || root.Name.NamespaceName.Length != 0)
            {
                throw new ArgumentException("The XML root element must be Program.", nameof(xml));
            }

            return new VizzyProgramDocument(root);
        }

        public VizzyProgramDocument Clone()
        {
            return new VizzyProgramDocument(new XElement(Root));
        }

        public string ToXml()
        {
            return CanonicalXml.Write(Root);
        }

        public XElement? FindById(int id)
        {
            foreach (var element in Root.DescendantsAndSelf())
            {
                var idAttribute = element.Attribute("id");
                if (idAttribute != null && int.TryParse(idAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var elementId) && elementId == id)
                {
                    return element;
                }
            }

            return null;
        }

        public XElement? FindByPath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            var segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !TryParseSegment(segments[0], out var rootName, out var rootIndex) || rootIndex != 0 || !string.Equals(rootName, Root.Name.LocalName, StringComparison.Ordinal))
            {
                return null;
            }

            var current = Root;
            for (var segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
            {
                if (!TryParseSegment(segments[segmentIndex], out var name, out var index) || index < 0)
                {
                    return null;
                }

                var matches = current.Elements().Where(element => string.Equals(element.Name.LocalName, name, StringComparison.Ordinal));
                var next = matches.Skip(index).FirstOrDefault();
                if (next == null)
                {
                    return null;
                }

                current = next;
            }

            return current;
        }

        private static bool TryParseSegment(string segment, out string name, out int index)
        {
            name = string.Empty;
            index = 0;

            var openBracket = segment.LastIndexOf('[');
            if (openBracket <= 0 || !segment.EndsWith("]", StringComparison.Ordinal))
            {
                return false;
            }

            name = segment.Substring(0, openBracket);
            return int.TryParse(segment.Substring(openBracket + 1, segment.Length - openBracket - 2), NumberStyles.None, CultureInfo.InvariantCulture, out index);
        }
    }
}
