using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Validation
{
    public sealed class VizzyProgramValidator
    {
        private static readonly string[] RequiredContainers =
        {
            "Variables",
            "Instructions",
            "Expressions"
        };

        private readonly Func<string, ValidationIssue?> runtimeSerializerValidator;

        public VizzyProgramValidator(Func<string, ValidationIssue?> runtimeSerializerValidator)
        {
            this.runtimeSerializerValidator = runtimeSerializerValidator
                ?? throw new ArgumentNullException(nameof(runtimeSerializerValidator));
        }

        public ValidationReport Validate(VizzyProgramDocument document, VizzyNodeCatalog catalog)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            var issues = new List<ValidationIssue>();
            AddPureXmlIssues(document, catalog, issues);
            AddCatalogIssues(document, catalog, issues);

            var runtimeIssue = runtimeSerializerValidator(document.ToXml());
            if (runtimeIssue != null)
            {
                issues.Add(runtimeIssue);
            }

            return new ValidationReport(issues);
        }

        private static void AddPureXmlIssues(
            VizzyProgramDocument document,
            VizzyNodeCatalog catalog,
            ICollection<ValidationIssue> issues)
        {
            foreach (var requiredContainer in RequiredContainers)
            {
                var count = document.Root.Elements()
                    .Count(element => HasUnqualifiedName(element, requiredContainer));
                if (count != 1)
                {
                    issues.Add(
                        Error(
                            "MissingContainer",
                            "Program must contain exactly one direct " + requiredContainer +
                                " container; found " + count.ToString(CultureInfo.InvariantCulture) + ".",
                            "/Program[0]"));
                }
            }

            AddIdIssues(document, issues);
            AddUnresolvedVariableIssues(document, issues);
            AddMalformedConstantIssues(document, issues);
            AddPlacementIssues(document, catalog, issues);
        }

        private static void AddIdIssues(VizzyProgramDocument document, ICollection<ValidationIssue> issues)
        {
            var ids = new HashSet<int>();
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                var attribute = element.Attribute("id");
                if (attribute == null)
                {
                    continue;
                }

                if (!int.TryParse(
                        attribute.Value,
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out var id) ||
                    !string.Equals(id.ToString(CultureInfo.InvariantCulture), attribute.Value, StringComparison.Ordinal))
                {
                    issues.Add(
                        Error(
                            "InvalidId",
                            "Id '" + attribute.Value + "' must be an exact Int32 integer.",
                            PathFor(element)));
                    continue;
                }

                if (!ids.Add(id))
                {
                    issues.Add(
                        Error(
                            "DuplicateId",
                            "Program contains duplicate id " + id.ToString(CultureInfo.InvariantCulture) + ".",
                            PathFor(element)));
                }
            }
        }

        private static void AddUnresolvedVariableIssues(VizzyProgramDocument document, ICollection<ValidationIssue> issues)
        {
            var variablesContainers = document.Root.Elements()
                .Where(element => HasUnqualifiedName(element, "Variables"))
                .Take(2)
                .ToList();
            if (variablesContainers.Count != 1)
            {
                return;
            }

            var declarations = variablesContainers[0].Elements()
                .Where(element => HasUnqualifiedName(element, "Variable"))
                .Select(element => element.Attribute("name")?.Value)
                .Where(name => name != null)
                .GroupBy(name => name!, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

            foreach (var element in document.Root.DescendantsAndSelf())
            {
                if (!string.Equals(element.Attribute("local")?.Value, "false", StringComparison.Ordinal))
                {
                    continue;
                }

                var variableName = element.Attribute("variableName")?.Value;
                if (variableName == null || !declarations.TryGetValue(variableName, out var count) || count != 1)
                {
                    issues.Add(
                        Error(
                            "UnresolvedVariable",
                            "Global variable reference '" + (variableName ?? string.Empty) + "' does not resolve exactly once.",
                            PathFor(element)));
                }
            }
        }

        private static void AddMalformedConstantIssues(VizzyProgramDocument document, ICollection<ValidationIssue> issues)
        {
            foreach (var constant in document.Root.DescendantsAndSelf().Where(element => HasUnqualifiedName(element, "Constant")))
            {
                var text = constant.Attribute("text");
                var number = constant.Attribute("number");
                var boolean = constant.Attribute("bool");
                var valueCount = (text != null ? 1 : 0) + (number != null ? 1 : 0) + (boolean != null ? 1 : 0);
                var validNumber = number == null ||
                    (double.TryParse(number.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
                     !double.IsNaN(parsed) &&
                     !double.IsInfinity(parsed));
                var validBoolean = boolean == null ||
                    string.Equals(boolean.Value, "true", StringComparison.Ordinal) ||
                    string.Equals(boolean.Value, "false", StringComparison.Ordinal);

                if (valueCount != 1 || !validNumber || !validBoolean)
                {
                    issues.Add(
                        Error(
                            "MalformedConstant",
                            "Constant must define exactly one valid text, number, or bool value.",
                            PathFor(constant)));
                }
            }
        }

        private static void AddPlacementIssues(
            VizzyProgramDocument document,
            VizzyNodeCatalog catalog,
            ICollection<ValidationIssue> issues)
        {
            var reported = new HashSet<XElement>();

            foreach (var container in document.Root.DescendantsAndSelf()
                .Where(element => RequiredContainers.Contains(element.Name.LocalName, StringComparer.Ordinal)))
            {
                var parent = container.Parent;
                var validParent = HasUnqualifiedName(container, "Instructions")
                    ? ReferenceEquals(parent, document.Root) ||
                      (parent != null && catalog.ContainsInstructionElement(parent.Name.LocalName))
                    : ReferenceEquals(parent, document.Root);
                if (!validParent)
                {
                    AddPlacementIssue(container, reported, issues);
                }
            }

            foreach (var instructions in document.Root.DescendantsAndSelf().Where(element => HasUnqualifiedName(element, "Instructions")))
            {
                foreach (var child in instructions.Elements())
                {
                    if (catalog.ContainsExpressionElement(child.Name.LocalName) &&
                        !catalog.ContainsInstructionElement(child.Name.LocalName))
                    {
                        AddPlacementIssue(child, reported, issues);
                    }
                }
            }

            foreach (var expressions in document.Root.Elements().Where(element => HasUnqualifiedName(element, "Expressions")))
            {
                foreach (var child in expressions.Elements())
                {
                    if (catalog.ContainsInstructionElement(child.Name.LocalName) &&
                        !catalog.ContainsExpressionElement(child.Name.LocalName))
                    {
                        AddPlacementIssue(child, reported, issues);
                    }
                }
            }

            foreach (var variables in document.Root.DescendantsAndSelf().Where(element => HasUnqualifiedName(element, "Variables")))
            {
                foreach (var child in variables.Elements().Where(element => !HasUnqualifiedName(element, "Variable")))
                {
                    AddPlacementIssue(child, reported, issues);
                }
            }
        }

        private static void AddPlacementIssue(
            XElement element,
            ISet<XElement> reported,
            ICollection<ValidationIssue> issues)
        {
            if (reported.Add(element))
            {
                issues.Add(
                    Error(
                        "InvalidChildPlacement",
                        element.Name.LocalName + " is not supported in its direct parent position.",
                        PathFor(element)));
            }
        }

        private static void AddCatalogIssues(
            VizzyProgramDocument document,
            VizzyNodeCatalog catalog,
            ICollection<ValidationIssue> issues)
        {
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                var style = element.Attribute("style");
                if (style != null && !catalog.ContainsStyle(style.Value))
                {
                    issues.Add(
                        Error(
                            "UnknownStyle",
                            "Style '" + style.Value + "' is not present in the node catalog.",
                            PathFor(element)));
                }
            }
        }

        private static ValidationIssue Error(string code, string message, string path)
        {
            return new ValidationIssue(ValidationSeverity.Error, code, message, path);
        }

        private static bool HasUnqualifiedName(XElement element, string name)
        {
            return element.Name.NamespaceName.Length == 0 &&
                string.Equals(element.Name.LocalName, name, StringComparison.Ordinal);
        }

        private static string PathFor(XElement element)
        {
            var segments = element.AncestorsAndSelf()
                .Reverse()
                .Select(current =>
                {
                    var index = current.ElementsBeforeSelf()
                        .Count(sibling => sibling.Name == current.Name);
                    return current.Name.LocalName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                });
            return "/" + string.Join("/", segments);
        }
    }
}
