using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using Newtonsoft.Json;

namespace VizzyGPT.Core.Patching
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NodeSpec
    {
        [JsonConstructor]
        public NodeSpec(string element, IReadOnlyDictionary<string, string> attributes, IReadOnlyList<NodeSpec> children)
        {
            if (element == null)
            {
                throw new PatchApplyException("Node element is required.");
            }

            if (attributes == null)
            {
                throw new PatchApplyException("Node attributes are required.");
            }

            if (children == null)
            {
                throw new PatchApplyException("Node children are required.");
            }

            var attributeCopy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var attribute in attributes)
            {
                if (!attributeCopy.TryAdd(attribute.Key, attribute.Value))
                {
                    throw new PatchApplyException("Node attributes contain duplicate key '" + attribute.Key + "'.");
                }
            }

            if (children.Any(child => child == null))
            {
                throw new PatchApplyException("Node children cannot contain null entries.");
            }

            Element = element;
            Attributes = new ReadOnlyDictionary<string, string>(attributeCopy);
            Children = new ReadOnlyCollection<NodeSpec>(children.ToArray());
        }

        [JsonProperty("element", Required = Required.Always)]
        public string Element { get; }

        [JsonProperty("attributes", Required = Required.Always)]
        public IReadOnlyDictionary<string, string> Attributes { get; }

        [JsonProperty("children", Required = Required.Always)]
        public IReadOnlyList<NodeSpec> Children { get; }

        public XElement ToXElement()
        {
            ValidateUnqualifiedName(Element, "element");
            var result = new XElement(Element);
            foreach (var attribute in Attributes)
            {
                ValidateUnqualifiedName(attribute.Key, "attribute");
                if (attribute.Value == null)
                {
                    throw new PatchApplyException("Node attribute '" + attribute.Key + "' cannot be null.");
                }

                if (string.Equals(attribute.Key, "id", StringComparison.Ordinal) &&
                    !int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    throw new PatchApplyException("Node id attribute must be a 32-bit integer.");
                }

                try
                {
                    result.Add(new XAttribute(attribute.Key, attribute.Value));
                }
                catch (ArgumentException exception)
                {
                    throw new PatchApplyException("Invalid node attribute '" + attribute.Key + "'.", exception);
                }
            }

            foreach (var child in Children)
            {
                result.Add(child.ToXElement());
            }

            return result;
        }

        private static void ValidateUnqualifiedName(string name, string kind)
        {
            if (string.IsNullOrEmpty(name))
            {
                throw new PatchApplyException("Node " + kind + " name cannot be empty.");
            }

            try
            {
                XmlConvert.VerifyNCName(name);
            }
            catch (XmlException exception)
            {
                throw new PatchApplyException("Node " + kind + " name '" + name + "' is invalid or namespace-qualified.", exception);
            }
        }
    }
}
