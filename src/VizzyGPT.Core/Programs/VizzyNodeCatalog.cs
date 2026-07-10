using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;

namespace VizzyGPT.Core.Programs
{
    public sealed class VizzyNodeCatalog
    {
        private readonly ImmutableOrdinalStringSet styles;
        private readonly ImmutableOrdinalStringSet elements;

        private VizzyNodeCatalog(ImmutableOrdinalStringSet styles, ImmutableOrdinalStringSet elements)
        {
            this.styles = styles;
            this.elements = elements;
        }

        public static VizzyNodeCatalog FromToolboxXml(string xml)
        {
            if (xml == null)
            {
                throw new ArgumentNullException(nameof(xml));
            }

            var root = XElement.Parse(xml, LoadOptions.PreserveWhitespace);
            var styles = root.Descendants("Style")
                .Select(style => style.Attribute("id")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Cast<string>();
            var elements = root.DescendantsAndSelf()
                .Select(element => element.Name.LocalName);

            return new VizzyNodeCatalog(new ImmutableOrdinalStringSet(styles), new ImmutableOrdinalStringSet(elements));
        }

        public bool ContainsStyle(string? style)
        {
            return style != null && styles.Contains(style);
        }

        public bool ContainsElement(string? element)
        {
            return element != null && elements.Contains(element);
        }

        private sealed class ImmutableOrdinalStringSet
        {
            private readonly string[] values;

            public ImmutableOrdinalStringSet(IEnumerable<string> source)
            {
                if (source == null)
                {
                    throw new ArgumentNullException(nameof(source));
                }

                values = new HashSet<string>(source, StringComparer.Ordinal).ToArray();
                Array.Sort(values, StringComparer.Ordinal);
            }

            public bool Contains(string value)
            {
                return Array.BinarySearch(values, value, StringComparer.Ordinal) >= 0;
            }
        }
    }
}
