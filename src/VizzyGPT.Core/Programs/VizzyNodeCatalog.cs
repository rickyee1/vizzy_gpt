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
            var styleColors = root.Elements("Styles")
                .SelectMany(container => container.Elements("Style"))
                .Where(style => style.Attribute("id") != null && style.Attribute("color") != null)
                .GroupBy(style => style.Attribute("id")!.Value, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    group => group.First().Attribute("color")!.Value,
                    StringComparer.Ordinal);
            var stockNodes = root.Elements("Categories")
                .SelectMany(container => container.Elements("Category"))
                .SelectMany(category => category.Elements())
                .ToArray();
            var instructionElements = CategoryElements(root, "Instructions")
                .Concat(StockCategoryElements(stockNodes, styleColors, IsInstructionColor));
            var expressionElements = CategoryElements(root, "Expressions")
                .Concat(StockCategoryElements(stockNodes, styleColors, IsExpressionColor));

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

        private static IEnumerable<string> StockCategoryElements(
            IEnumerable<XElement> nodes,
            IReadOnlyDictionary<string, string> styleColors,
            Func<string, bool> acceptsColor)
        {
            return nodes
                .Where(node =>
                {
                    var style = node.Attribute("style")?.Value;
                    return style != null &&
                        styleColors.TryGetValue(style, out var color) &&
                        acceptsColor(color);
                })
                .Select(node => node.Name.LocalName);
        }

        private static bool IsInstructionColor(string color)
        {
            return string.Equals(color, "Instruction", StringComparison.Ordinal) ||
                string.Equals(color, "InstructionBlock", StringComparison.Ordinal) ||
                string.Equals(color, "CraftInstruction", StringComparison.Ordinal) ||
                string.Equals(color, "Event", StringComparison.Ordinal) ||
                string.Equals(color, "CustomInstruction", StringComparison.Ordinal) ||
                string.Equals(color, "WidgetInstruction", StringComparison.Ordinal);
        }

        private static bool IsExpressionColor(string color)
        {
            return !IsInstructionColor(color);
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
