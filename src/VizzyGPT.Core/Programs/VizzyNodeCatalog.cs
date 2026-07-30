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
        private readonly ImmutableOrdinalStringSet styledElements;
        private readonly ImmutableOrdinalStringSet elementStylePairs;
        private readonly string[] templates;

        private VizzyNodeCatalog(
            ImmutableOrdinalStringSet styles,
            ImmutableOrdinalStringSet elements,
            ImmutableOrdinalStringSet instructionElements,
            ImmutableOrdinalStringSet expressionElements,
            ImmutableOrdinalStringSet styledElements,
            ImmutableOrdinalStringSet elementStylePairs,
            IEnumerable<string> templates)
        {
            this.styles = styles;
            this.elements = elements;
            this.instructionElements = instructionElements;
            this.expressionElements = expressionElements;
            this.styledElements = styledElements;
            this.elementStylePairs = elementStylePairs;
            this.templates = DistinctOrdinal(templates);
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
            var concreteTemplates = stockNodes.Any()
                ? stockNodes
                : CompactTemplates(root).ToArray();
            var styledTemplateNodes = concreteTemplates
                .SelectMany(template => template.DescendantsAndSelf())
                .Where(node => node.Attribute("style") != null)
                .ToArray();

            return new VizzyNodeCatalog(
                new ImmutableOrdinalStringSet(styles),
                new ImmutableOrdinalStringSet(elements),
                new ImmutableOrdinalStringSet(instructionElements),
                new ImmutableOrdinalStringSet(expressionElements),
                new ImmutableOrdinalStringSet(styledTemplateNodes.Select(node => node.Name.LocalName)),
                new ImmutableOrdinalStringSet(styledTemplateNodes.Select(node =>
                    ElementStylePairKey(node.Name.LocalName, node.Attribute("style")!.Value))),
                concreteTemplates.Select(CanonicalTemplate));
        }

        public IReadOnlyList<string> Templates => Array.AsReadOnly(templates);

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

        public bool ContainsStyledElement(string? element)
        {
            return element != null && styledElements.Contains(element);
        }

        public bool ContainsElementStylePair(string? element, string? style)
        {
            return element != null &&
                style != null &&
                elementStylePairs.Contains(ElementStylePairKey(element, style));
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

        private static IEnumerable<XElement> CompactTemplates(XElement root)
        {
            return root.Elements()
                .Where(element =>
                    element.Name.NamespaceName.Length == 0 &&
                    (string.Equals(element.Name.LocalName, "Instructions", StringComparison.Ordinal) ||
                     string.Equals(element.Name.LocalName, "Expressions", StringComparison.Ordinal)))
                .SelectMany(container => container.Elements());
        }

        private static string CanonicalTemplate(XElement node)
        {
            var clone = new XElement(node);
            CanonicalizeTemplateElement(clone);
            return clone.ToString(SaveOptions.DisableFormatting);
        }

        private static void CanonicalizeTemplateElement(XElement element)
        {
            element.ReplaceAttributes(element.Attributes()
                .OrderBy(attribute => attribute.Name.LocalName, StringComparer.Ordinal)
                .ThenBy(attribute => attribute.Name.NamespaceName, StringComparer.Ordinal)
                .Select(attribute => new XAttribute(attribute)));

            var textNodes = element.Nodes().OfType<XText>().ToArray();
            if (textNodes.All(text => string.IsNullOrWhiteSpace(text.Value)))
            {
                foreach (var textNode in textNodes)
                {
                    textNode.Remove();
                }
            }

            foreach (var child in element.Elements())
            {
                CanonicalizeTemplateElement(child);
            }
        }

        private static string[] DistinctOrdinal(IEnumerable<string> source)
        {
            var values = new HashSet<string>(source, StringComparer.Ordinal).ToArray();
            Array.Sort(values, StringComparer.Ordinal);
            return values;
        }

        private static string ElementStylePairKey(string element, string style)
        {
            return element + "\0" + style;
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

                values = DistinctOrdinal(source);
            }

            public bool Contains(string value)
            {
                return Array.BinarySearch(values, value, StringComparer.Ordinal) >= 0;
            }
        }
    }
}
