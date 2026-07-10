using System;
using System.Linq;
using System.Xml.Linq;

namespace VizzyGPT.Core.Programs
{
    public static class CanonicalXml
    {
        public static string Write(XElement element)
        {
            if (element == null)
            {
                throw new ArgumentNullException(nameof(element));
            }

            return CreateCanonicalElement(element).ToString(SaveOptions.DisableFormatting);
        }

        private static XElement CreateCanonicalElement(XElement element)
        {
            var canonical = new XElement(element.Name);
            foreach (var attribute in element.Attributes().OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal))
            {
                canonical.Add(new XAttribute(attribute));
            }

            var hasElementChildren = element.Elements().Any();
            var hasSignificantMixedText = element.Nodes().OfType<XText>().Any(text => !(text is XCData) && !string.IsNullOrWhiteSpace(text.Value));
            foreach (var node in element.Nodes())
            {
                if (node is XElement childElement)
                {
                    canonical.Add(CreateCanonicalElement(childElement));
                }
                else if (node is XCData cdata)
                {
                    canonical.Add(new XCData(cdata.Value));
                }
                else if (node is XText text)
                {
                    if (!hasElementChildren || hasSignificantMixedText || !string.IsNullOrWhiteSpace(text.Value))
                    {
                        canonical.Add(new XText(text.Value));
                    }
                }
                else if (node is XComment comment)
                {
                    canonical.Add(new XComment(comment.Value));
                }
                else if (node is XProcessingInstruction instruction)
                {
                    canonical.Add(new XProcessingInstruction(instruction.Target, instruction.Data));
                }
            }

            return canonical;
        }
    }
}
