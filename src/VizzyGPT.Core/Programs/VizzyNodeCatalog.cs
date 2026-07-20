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
        private readonly ImmutableOrdinalStringSet instructionElements;
        private readonly ImmutableOrdinalStringSet expressionElements;

        private VizzyNodeCatalog(
            ImmutableOrdinalStringSet styles,
            ImmutableOrdinalStringSet elements,
            ImmutableOrdinalStringSet instructionElements,
            ImmutableOrdinalStringSet expressionElements)
        {
            this.styles = styles;
            this.elements = elements;
            this.instructionElements = instructionElements;
            this.expressionElements = expressionElements;
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
            var instructionElements = CategoryElements(root, "Instructions");
            var expressionElements = CategoryElements(root, "Expressions");

            return new VizzyNodeCatalog(
                new ImmutableOrdinalStringSet(styles),
                new ImmutableOrdinalStringSet(elements),
                new ImmutableOrdinalStringSet(instructionElements),
                new ImmutableOrdinalStringSet(expressionElements));
        }

        public bool ContainsStyle(string? style)
        {
            return style != null && styles.Contains(style);
        }

        public bool ContainsElement(string? element)
        {
            return element != null && elements.Contains(element);
        }

        public bool ContainsInstructionElement(string? element)
        {
            return element != null && instructionElements.Contains(element);
        }

        public bool ContainsExpressionElement(string? element)
        {
            return element != null && expressionElements.Contains(element);
        }

        private static IEnumerable<string> CategoryElements(XElement root, string categoryName)
        {
            return root.Elements()
                .Where(element =>
                    element.Name.NamespaceName.Length == 0 &&
                    string.Equals(element.Name.LocalName, categoryName, StringComparison.Ordinal))
                .SelectMany(category => category.Elements())
                .Select(element => element.Name.LocalName);
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
