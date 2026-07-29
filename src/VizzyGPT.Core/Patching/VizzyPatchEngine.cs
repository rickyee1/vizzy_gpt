using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Patching
{
    public sealed class PatchApplyException : Exception
    {
        public PatchApplyException(string message)
            : base(message)
        {
        }

        public PatchApplyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    public sealed class PatchResult
    {
        public PatchResult(VizzyProgramDocument document, IReadOnlyList<string> changes)
        {
            Document = document ?? throw new ArgumentNullException(nameof(document));
            if (changes == null)
            {
                throw new ArgumentNullException(nameof(changes));
            }

            Changes = new ReadOnlyCollection<string>(changes.ToArray());
        }

        public VizzyProgramDocument Document { get; }

        public IReadOnlyList<string> Changes { get; }
    }

    public static class VizzyPatchEngine
    {
        private static readonly HashSet<string> UpdatableAttributes = new HashSet<string>(
            new[] { "text", "number", "bool", "style", "event", "property", "op", "variableName", "pos" },
            StringComparer.Ordinal);

        private static readonly HashSet<string> ProtectedRootNames = new HashSet<string>(
            new[] { "Variables", "Instructions", "Expressions" },
            StringComparer.Ordinal);

        public static PatchResult Apply(VizzyProgramDocument document, PatchDocument patch)
        {
            if (document == null)
            {
                throw new PatchApplyException("Input program document cannot be null.");
            }

            if (patch == null)
            {
                throw new PatchApplyException("Patch document cannot be null.");
            }

            var currentHash = VizzyProgramHash.Compute(document);
            if (!string.Equals(currentHash, patch.BaseHash, StringComparison.Ordinal))
            {
                throw new PatchApplyException("Patch base hash does not match the input program.");
            }

            var resultDocument = document.Clone();
            var changes = new List<string>(patch.Operations.Count);
            foreach (var operation in patch.Operations)
            {
                changes.Add(ApplyOperation(resultDocument, operation));
            }

            RejectDuplicateIds(resultDocument);
            return new PatchResult(resultDocument, changes);
        }

        private static string ApplyOperation(VizzyProgramDocument document, PatchOperation operation)
        {
            switch (operation.Type)
            {
                case PatchOperationType.AddVariable:
                    return AddVariable(document, operation);
                case PatchOperationType.RenameVariable:
                    return RenameVariable(document, operation);
                case PatchOperationType.RemoveVariable:
                    return RemoveVariable(document, operation);
                case PatchOperationType.InsertBefore:
                    return InsertSibling(document, operation, before: true);
                case PatchOperationType.InsertAfter:
                    return InsertSibling(document, operation, before: false);
                case PatchOperationType.InsertChild:
                    return InsertChild(document, operation);
                case PatchOperationType.ReplaceNode:
                    return ReplaceNode(document, operation);
                case PatchOperationType.RemoveNode:
                    return RemoveNode(document, operation);
                case PatchOperationType.MoveNode:
                    return MoveNode(document, operation);
                case PatchOperationType.UpdateAttribute:
                    return UpdateAttribute(document, operation);
                default:
                    throw new PatchApplyException("Unknown patch operation type.");
            }
        }

        private static string AddVariable(VizzyProgramDocument document, PatchOperation operation)
        {
            var variables = GetVariablesContainer(document);
            var name = operation.Name!;
            var value = operation.Value ?? "0";
            if (FindVariables(variables, name).Count != 0)
            {
                throw new PatchApplyException("Variable '" + name + "' already exists.");
            }

            ValidateXmlAttributeValue(name, "Variable name");
            ValidateXmlAttributeValue(value, "Variable value");
            variables.Add(
                new XElement(
                    "Variable",
                    new XAttribute("name", name),
                    new XAttribute("number", value)));
            return "Added variable '" + SingleLine(name) + "'.";
        }

        private static string RenameVariable(VizzyProgramDocument document, PatchOperation operation)
        {
            var variables = GetVariablesContainer(document);
            var name = operation.Name!;
            var newName = operation.NewName!;
            var declarations = FindVariables(variables, name);
            if (declarations.Count != 1)
            {
                throw new PatchApplyException("Variable '" + name + "' must resolve to exactly one direct declaration.");
            }

            if (FindVariables(variables, newName).Any(declaration => !ReferenceEquals(declaration, declarations[0])))
            {
                throw new PatchApplyException("Variable '" + newName + "' already exists.");
            }

            ValidateXmlAttributeValue(newName, "New variable name");
            declarations[0].SetAttributeValue("name", newName);
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                var reference = element.Attribute("variableName");
                if (reference != null && string.Equals(reference.Value, name, StringComparison.Ordinal))
                {
                    reference.Value = newName;
                }
            }

            return "Renamed variable '" + SingleLine(name) + "' to '" + SingleLine(newName) + "'.";
        }

        private static string RemoveVariable(VizzyProgramDocument document, PatchOperation operation)
        {
            var variables = GetVariablesContainer(document);
            var name = operation.Name!;
            var declarations = FindVariables(variables, name);
            if (declarations.Count != 1)
            {
                throw new PatchApplyException("Variable '" + name + "' must resolve to exactly one direct declaration.");
            }

            declarations[0].Remove();
            return "Removed variable '" + SingleLine(name) + "'.";
        }

        private static string InsertSibling(VizzyProgramDocument document, PatchOperation operation, bool before)
        {
            var target = Resolve(document, operation.Target!);
            if (target.Parent == null || !HasUnqualifiedName(target.Parent, "Instructions"))
            {
                throw new PatchApplyException("Sibling insertion targets must be direct children of an Instructions container.");
            }

            RejectProtectedNodeSpec(operation.Node!);
            var node = operation.Node!.ToXElement();
            if (before)
            {
                target.AddBeforeSelf(node);
            }
            else
            {
                target.AddAfterSelf(node);
            }

            return "Inserted <" + SingleLine(operation.Node.Element) + "> " + (before ? "before " : "after ") + Describe(operation.Target!) + ".";
        }

        private static string InsertChild(VizzyProgramDocument document, PatchOperation operation)
        {
            var target = Resolve(document, operation.Target!);
            RequireInstructionsContainer(target, "Child insertion target");
            RejectProtectedNodeSpec(operation.Node!);
            target.Add(operation.Node!.ToXElement());
            return "Inserted <" + SingleLine(operation.Node.Element) + "> into " + Describe(operation.Target!) + ".";
        }

        private static string ReplaceNode(VizzyProgramDocument document, PatchOperation operation)
        {
            var target = Resolve(document, operation.Target!);
            RejectProtectedRoot(target, document, "Replacement target");
            RequireEditableSubtree(document, target, "Replacement target");
            RejectProtectedNodeSpec(operation.Node!);

            var replacement = operation.Node!.ToXElement();
            if (replacement.Attribute("pos") == null && target.Attribute("pos") is XAttribute targetPosition)
            {
                replacement.Add(new XAttribute(targetPosition));
            }

            target.ReplaceWith(replacement);
            return "Replaced " + Describe(operation.Target!) + " with <" + SingleLine(operation.Node.Element) + ">.";
        }

        private static string RemoveNode(VizzyProgramDocument document, PatchOperation operation)
        {
            var target = Resolve(document, operation.Target!);
            RejectProtectedRoot(target, document, "Removal target");
            RequireEditableSubtree(document, target, "Removal target");
            target.Remove();
            return "Removed node " + Describe(operation.Target!) + ".";
        }

        private static string MoveNode(VizzyProgramDocument document, PatchOperation operation)
        {
            var target = Resolve(document, operation.Target!);
            var destination = Resolve(document, operation.Destination!);
            RejectProtectedRoot(target, document, "Move target");
            RequireEditableSubtree(document, target, "Move target");
            RequireInstructionsContainer(destination, "Move destination");

            if (ReferenceEquals(target, destination) || destination.Ancestors().Any(ancestor => ReferenceEquals(ancestor, target)))
            {
                throw new PatchApplyException("A node cannot be moved into itself or one of its descendants.");
            }

            target.Remove();
            destination.Add(target);
            return "Moved node " + Describe(operation.Target!) + " into " + Describe(operation.Destination!) + ".";
        }

        private static string UpdateAttribute(VizzyProgramDocument document, PatchOperation operation)
        {
            var target = Resolve(document, operation.Target!);
            var attribute = operation.Attribute!;
            if (!UpdatableAttributes.Contains(attribute))
            {
                throw new PatchApplyException("Attribute '" + attribute + "' cannot be updated by a patch.");
            }

            ValidateXmlAttributeValue(operation.Value!, "Attribute '" + attribute + "'");
            target.SetAttributeValue(attribute, operation.Value!);
            return "Updated attribute '" + SingleLine(attribute) + "' on " + Describe(operation.Target!) + ".";
        }

        private static XElement Resolve(VizzyProgramDocument document, NodeSelector selector)
        {
            if (selector.Id.HasValue)
            {
                var matches = document.Root
                    .DescendantsAndSelf()
                    .Where(element => HasId(element, selector.Id.Value))
                    .Take(2)
                    .ToList();
                if (matches.Count != 1)
                {
                    throw new PatchApplyException("Node selector " + Describe(selector) + " must resolve to exactly one node.");
                }

                return matches[0];
            }

            var match = document.FindByPath(selector.Path!);
            return match ?? throw new PatchApplyException("Node selector " + Describe(selector) + " did not resolve to a node.");
        }

        private static XElement GetVariablesContainer(VizzyProgramDocument document)
        {
            var matches = document.Root.Elements().Where(element => HasUnqualifiedName(element, "Variables")).Take(2).ToList();
            if (matches.Count != 1)
            {
                throw new PatchApplyException("Program must contain exactly one direct Variables container.");
            }

            return matches[0];
        }

        private static List<XElement> FindVariables(XElement variables, string name)
        {
            return variables
                .Elements()
                .Where(element =>
                    HasUnqualifiedName(element, "Variable") &&
                    string.Equals(element.Attribute("name")?.Value, name, StringComparison.Ordinal))
                .ToList();
        }

        private static void RequireInstructionsContainer(XElement element, string context)
        {
            if (!HasUnqualifiedName(element, "Instructions"))
            {
                throw new PatchApplyException(context + " must resolve to an Instructions container.");
            }
        }

        private static void RequireEditableSubtree(VizzyProgramDocument document, XElement element, string context)
        {
            var isEditable = element.Ancestors().Any(ancestor =>
                ReferenceEquals(ancestor.Parent, document.Root) &&
                (HasUnqualifiedName(ancestor, "Instructions") || HasUnqualifiedName(ancestor, "Expressions")));
            if (!isEditable)
            {
                throw new PatchApplyException(context + " must be inside an instruction or expression subtree and cannot be a structural root.");
            }
        }

        private static void RejectProtectedRoot(XElement target, VizzyProgramDocument document, string context)
        {
            if (ReferenceEquals(target.Parent, document.Root) &&
                ProtectedRootNames.Contains(target.Name.LocalName))
            {
                throw new PatchApplyException(context + " cannot target a protected structural root.");
            }
        }

        private static void RejectProtectedNodeSpec(NodeSpec node)
        {
            if (ProtectedRootNames.Contains(node.Element))
            {
                throw new PatchApplyException(
                    "Patch node cannot create a protected structural root inside an editable subtree.");
            }
        }

        private static void RejectDuplicateIds(VizzyProgramDocument document)
        {
            var ids = new HashSet<int>();
            foreach (var element in document.Root.DescendantsAndSelf())
            {
                var id = element.Attribute("id");
                if (id != null && int.TryParse(id.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && !ids.Add(value))
                {
                    throw new PatchApplyException("Patched program contains duplicate id " + value.ToString(CultureInfo.InvariantCulture) + ".");
                }
            }
        }

        private static bool HasId(XElement element, int id)
        {
            var attribute = element.Attribute("id");
            return attribute != null &&
                int.TryParse(attribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                value == id;
        }

        private static bool HasUnqualifiedName(XElement element, string name)
        {
            return element.Name.NamespaceName.Length == 0 && string.Equals(element.Name.LocalName, name, StringComparison.Ordinal);
        }

        private static void ValidateXmlAttributeValue(string value, string context)
        {
            try
            {
                XmlConvert.VerifyXmlChars(value);
            }
            catch (XmlException exception)
            {
                throw new PatchApplyException(context + " contains characters that are not valid in XML.", exception);
            }
        }

        private static string Describe(NodeSelector selector)
        {
            return selector.Id.HasValue
                ? "id " + selector.Id.Value.ToString(CultureInfo.InvariantCulture)
                : "path '" + SingleLine(selector.Path!) + "'";
        }

        private static string SingleLine(string value)
        {
            return value.Replace('\r', ' ').Replace('\n', ' ');
        }
    }
}
