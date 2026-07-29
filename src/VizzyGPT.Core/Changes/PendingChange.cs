using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Changes
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class TargetFingerprint
    {
        [JsonConstructor]
        public TargetFingerprint(NodeSelector selector, string hash)
        {
            if (selector == null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            if (string.IsNullOrEmpty(hash))
            {
                throw new ArgumentException("Target fingerprint hash must be non-empty.", nameof(hash));
            }

            Selector = new NodeSelector(selector.Id, selector.Path);
            Hash = hash;
        }

        [JsonProperty("selector", Required = Required.Always)]
        public NodeSelector Selector { get; }

        [JsonProperty("hash", Required = Required.Always)]
        public string Hash { get; }
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum DeclarationKind
    {
        [EnumMember(Value = "variable")]
        Variable,
        [EnumMember(Value = "customNode")]
        CustomNode
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class DeclarationFingerprint
    {
        [JsonConstructor]
        public DeclarationFingerprint(DeclarationKind kind, string name, string hash)
        {
            if (!Enum.IsDefined(typeof(DeclarationKind), kind))
            {
                throw new ArgumentOutOfRangeException(nameof(kind));
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new ArgumentException("Declaration fingerprint name must be non-empty.", nameof(name));
            }

            if (string.IsNullOrEmpty(hash))
            {
                throw new ArgumentException("Declaration fingerprint hash must be non-empty.", nameof(hash));
            }

            Kind = kind;
            Name = name;
            Hash = hash;
        }

        [JsonProperty("kind", Required = Required.Always)]
        public DeclarationKind Kind { get; }

        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; }

        [JsonProperty("hash", Required = Required.Always)]
        public string Hash { get; }
    }

    [JsonObject(MemberSerialization.OptIn)]
    public sealed class PendingChange
    {
        [JsonConstructor]
        public PendingChange(
            string programFingerprint,
            string baseXml,
            string baseHash,
            string patchJson,
            string resultXml,
            string resultHash,
            IReadOnlyList<TargetFingerprint> targetFingerprints,
            IReadOnlyList<DeclarationFingerprint> declarationFingerprints,
            DateTime createdUtc,
            IReadOnlyList<string> previewLines)
        {
            if (string.IsNullOrWhiteSpace(programFingerprint))
            {
                throw new ArgumentException("Program fingerprint must be non-whitespace.", nameof(programFingerprint));
            }

            if (baseXml == null)
            {
                throw new ArgumentNullException(nameof(baseXml));
            }

            if (string.IsNullOrEmpty(baseHash))
            {
                throw new ArgumentException("Base hash must be non-empty.", nameof(baseHash));
            }

            if (string.IsNullOrWhiteSpace(patchJson))
            {
                throw new ArgumentException("Patch JSON must be non-whitespace.", nameof(patchJson));
            }

            if (resultXml == null)
            {
                throw new ArgumentNullException(nameof(resultXml));
            }

            if (string.IsNullOrEmpty(resultHash))
            {
                throw new ArgumentException("Result hash must be non-empty.", nameof(resultHash));
            }

            if (targetFingerprints == null)
            {
                throw new ArgumentNullException(nameof(targetFingerprints));
            }

            if (targetFingerprints.Any(fingerprint => fingerprint == null))
            {
                throw new ArgumentException("Target fingerprints cannot contain null entries.", nameof(targetFingerprints));
            }

            if (declarationFingerprints == null)
            {
                throw new ArgumentNullException(nameof(declarationFingerprints));
            }

            if (declarationFingerprints.Any(fingerprint => fingerprint == null))
            {
                throw new ArgumentException("Declaration fingerprints cannot contain null entries.", nameof(declarationFingerprints));
            }

            if (createdUtc.Kind != DateTimeKind.Utc)
            {
                throw new ArgumentException("Pending change timestamp must be UTC.", nameof(createdUtc));
            }

            if (previewLines == null)
            {
                throw new ArgumentNullException(nameof(previewLines));
            }

            if (previewLines.Any(line => line == null))
            {
                throw new ArgumentException("Preview lines cannot contain null entries.", nameof(previewLines));
            }

            var parsedBase = VizzyProgramDocument.Parse(baseXml);
            if (!string.Equals(VizzyProgramHash.Compute(parsedBase), baseHash, StringComparison.Ordinal))
            {
                throw new ArgumentException("Base XML does not match the persisted base hash.", nameof(baseHash));
            }

            var parsedResult = VizzyProgramDocument.Parse(resultXml);
            if (!string.Equals(VizzyProgramHash.Compute(parsedResult), resultHash, StringComparison.Ordinal))
            {
                throw new ArgumentException("Result XML does not match the persisted result hash.", nameof(resultHash));
            }

            var parsedPatch = PatchDocument.Deserialize(patchJson);
            if (!string.Equals(parsedPatch.BaseHash, baseHash, StringComparison.Ordinal))
            {
                throw new ArgumentException("Patch JSON does not match the persisted base hash.", nameof(patchJson));
            }

            var applied = VizzyPatchEngine.Apply(parsedBase, parsedPatch);
            if (!string.Equals(applied.Document.ToXml(), parsedResult.ToXml(), StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Result XML does not match applying the persisted patch to the persisted base XML.",
                    nameof(resultXml));
            }

            if (previewLines.Count < applied.Changes.Count)
            {
                throw new ArgumentException(
                    "Preview lines must contain every deterministic patch change.",
                    nameof(previewLines));
            }

            for (var index = 0; index < applied.Changes.Count; index++)
            {
                if (!string.Equals(previewLines[index], applied.Changes[index], StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Preview lines must begin with the exact deterministic patch changes.",
                        nameof(previewLines));
                }
            }

            var expectedDependencies = PendingDependencyAnalyzer.Analyze(parsedBase, parsedPatch);
            RequireMatchingTargetFingerprints(expectedDependencies.TargetFingerprints, targetFingerprints);
            RequireMatchingDeclarationFingerprints(
                expectedDependencies.DeclarationFingerprints,
                declarationFingerprints);

            ProgramFingerprint = programFingerprint;
            BaseXml = baseXml;
            BaseHash = baseHash;
            PatchJson = patchJson;
            ResultXml = resultXml;
            ResultHash = resultHash;
            TargetFingerprints = new ReadOnlyCollection<TargetFingerprint>(
                targetFingerprints.Select(
                    fingerprint => new TargetFingerprint(fingerprint.Selector, fingerprint.Hash)).ToArray());
            DeclarationFingerprints = new ReadOnlyCollection<DeclarationFingerprint>(
                declarationFingerprints.Select(
                    fingerprint => new DeclarationFingerprint(fingerprint.Kind, fingerprint.Name, fingerprint.Hash)).ToArray());
            CreatedUtc = createdUtc;
            PreviewLines = new ReadOnlyCollection<string>(previewLines.ToArray());
        }

        [JsonProperty("programFingerprint", Required = Required.Always)]
        public string ProgramFingerprint { get; }

        [JsonProperty("baseXml", Required = Required.Always)]
        public string BaseXml { get; }

        [JsonProperty("baseHash", Required = Required.Always)]
        public string BaseHash { get; }

        [JsonProperty("patchJson", Required = Required.Always)]
        public string PatchJson { get; }

        [JsonProperty("resultXml", Required = Required.Always)]
        public string ResultXml { get; }

        [JsonProperty("resultHash", Required = Required.Always)]
        public string ResultHash { get; }

        [JsonProperty("targetFingerprints", Required = Required.Always)]
        public IReadOnlyList<TargetFingerprint> TargetFingerprints { get; }

        [JsonProperty("declarationFingerprints", Required = Required.Always)]
        public IReadOnlyList<DeclarationFingerprint> DeclarationFingerprints { get; }

        [JsonProperty("createdUtc", Required = Required.Always)]
        public DateTime CreatedUtc { get; }

        [JsonProperty("previewLines", Required = Required.Always)]
        public IReadOnlyList<string> PreviewLines { get; }

        public static PendingChange Create(string programFingerprint, ChangeSession session, DateTime createdUtc)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            var baseDocument = VizzyProgramDocument.Parse(session.BaseXml);
            var dependencies = PendingDependencyAnalyzer.Analyze(baseDocument, session.Patch);

            return new PendingChange(
                programFingerprint,
                session.BaseXml,
                session.BaseHash,
                JsonConvert.SerializeObject(session.Patch, Formatting.None),
                session.ResultXml,
                session.ResultHash,
                dependencies.TargetFingerprints,
                dependencies.DeclarationFingerprints,
                createdUtc,
                session.PreviewLines);
        }

        private static void RequireMatchingTargetFingerprints(
            IReadOnlyList<TargetFingerprint> expected,
            IReadOnlyList<TargetFingerprint> actual)
        {
            if (expected.Count != actual.Count)
            {
                throw new ArgumentException(
                    "Target fingerprints do not match the dependencies recomputed from the base and patch.",
                    nameof(actual));
            }

            for (var index = 0; index < expected.Count; index++)
            {
                var expectedItem = expected[index];
                var actualItem = actual[index];
                if (expectedItem.Selector.Id != actualItem.Selector.Id ||
                    !string.Equals(expectedItem.Selector.Path, actualItem.Selector.Path, StringComparison.Ordinal) ||
                    !string.Equals(expectedItem.Hash, actualItem.Hash, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Target fingerprints do not match the dependencies recomputed from the base and patch.",
                        nameof(actual));
                }
            }
        }

        private static void RequireMatchingDeclarationFingerprints(
            IReadOnlyList<DeclarationFingerprint> expected,
            IReadOnlyList<DeclarationFingerprint> actual)
        {
            if (expected.Count != actual.Count)
            {
                throw new ArgumentException(
                    "Declaration fingerprints do not match the dependencies recomputed from the base and patch.",
                    nameof(actual));
            }

            for (var index = 0; index < expected.Count; index++)
            {
                var expectedItem = expected[index];
                var actualItem = actual[index];
                if (expectedItem.Kind != actualItem.Kind ||
                    !string.Equals(expectedItem.Name, actualItem.Name, StringComparison.Ordinal) ||
                    !string.Equals(expectedItem.Hash, actualItem.Hash, StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        "Declaration fingerprints do not match the dependencies recomputed from the base and patch.",
                        nameof(actual));
                }
            }
        }
    }

    internal sealed class PendingDependencyAnalysis
    {
        public PendingDependencyAnalysis(
            IReadOnlyList<TargetFingerprint> targetFingerprints,
            IReadOnlyList<DeclarationFingerprint> declarationFingerprints)
        {
            TargetFingerprints = targetFingerprints;
            DeclarationFingerprints = declarationFingerprints;
        }

        public IReadOnlyList<TargetFingerprint> TargetFingerprints { get; }

        public IReadOnlyList<DeclarationFingerprint> DeclarationFingerprints { get; }
    }

    internal static class PendingDependencyAnalyzer
    {
        public static PendingDependencyAnalysis Analyze(
            VizzyProgramDocument baseDocument,
            PatchDocument patch)
        {
            var working = CreateAnnotatedClone(baseDocument);
            var targetFingerprints = new List<TargetFingerprint>();
            var targetKeys = new HashSet<string>(StringComparer.Ordinal);
            var declarations = new Dictionary<string, DeclarationFingerprint>(StringComparer.Ordinal);

            foreach (var operation in patch.Operations)
            {
                RecordSelector(
                    baseDocument,
                    working,
                    operation.Target,
                    targetFingerprints,
                    targetKeys,
                    declarations);
                RecordSelector(
                    baseDocument,
                    working,
                    operation.Destination,
                    targetFingerprints,
                    targetKeys,
                    declarations);

                if (operation.Node != null)
                {
                    var sameOperationDeclarations = GetSameOperationDeclarations(working, operation);
                    RecordReferences(
                        working,
                        operation.Node.ToXElement(),
                        declarations,
                        sameOperationDeclarations);
                }

                if (operation.Type == PatchOperationType.RenameVariable ||
                    operation.Type == PatchOperationType.RemoveVariable)
                {
                    RecordDeclaration(working, DeclarationKind.Variable, operation.Name!, declarations);
                }

                if (operation.Type == PatchOperationType.UpdateAttribute &&
                    string.Equals(operation.Attribute, "variableName", StringComparison.Ordinal))
                {
                    var target = ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!);
                    if (!string.Equals(target.Attribute("local")?.Value, "true", StringComparison.Ordinal))
                    {
                        RecordDeclaration(working, DeclarationKind.Variable, operation.Value!, declarations);
                    }
                }

                ApplyOperation(working, operation);
            }

            var orderedDeclarations = declarations.Values
                .OrderBy(declaration => declaration.Kind)
                .ThenBy(declaration => declaration.Name, StringComparer.Ordinal)
                .ToArray();
            return new PendingDependencyAnalysis(targetFingerprints.ToArray(), orderedDeclarations);
        }

        private static VizzyProgramDocument CreateAnnotatedClone(VizzyProgramDocument baseDocument)
        {
            var working = baseDocument.Clone();
            using var originals = baseDocument.Root.DescendantsAndSelf().GetEnumerator();
            using var clones = working.Root.DescendantsAndSelf().GetEnumerator();
            while (originals.MoveNext() && clones.MoveNext())
            {
                clones.Current.AddAnnotation(
                    new BaseOrigin(new XElement(originals.Current), ChangeFingerprintUtilities.PathFor(originals.Current)));
            }

            return working;
        }

        private static void RecordSelector(
            VizzyProgramDocument baseDocument,
            VizzyProgramDocument working,
            NodeSelector? selector,
            ICollection<TargetFingerprint> targetFingerprints,
            ISet<string> targetKeys,
            IDictionary<string, DeclarationFingerprint> declarations)
        {
            if (selector == null)
            {
                return;
            }

            var selected = ChangeFingerprintUtilities.ResolveSelector(working, selector);
            RecordReferences(working, selected, declarations);

            var origin = selected.Annotation<BaseOrigin>();
            if (origin == null)
            {
                return;
            }

            var baseSelector = SelectBaseOrigin(baseDocument, selector, origin);
            var key = baseSelector.Id.HasValue
                ? "id:" + baseSelector.Id.Value.ToString(CultureInfo.InvariantCulture)
                : "path:" + baseSelector.Path;
            if (targetKeys.Add(key))
            {
                targetFingerprints.Add(
                    new TargetFingerprint(baseSelector, ChangeFingerprintUtilities.ComputeHash(origin.Snapshot)));
            }
        }

        private static NodeSelector SelectBaseOrigin(
            VizzyProgramDocument baseDocument,
            NodeSelector operationSelector,
            BaseOrigin origin)
        {
            try
            {
                var direct = ChangeFingerprintUtilities.ResolveSelector(baseDocument, operationSelector);
                if (string.Equals(ChangeFingerprintUtilities.PathFor(direct), origin.Path, StringComparison.Ordinal))
                {
                    return new NodeSelector(operationSelector.Id, operationSelector.Path);
                }
            }
            catch (InvalidOperationException)
            {
            }

            var idAttribute = origin.Snapshot.Attribute("id");
            if (idAttribute != null &&
                int.TryParse(idAttribute.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                var idSelector = new NodeSelector(id, null);
                try
                {
                    var selected = ChangeFingerprintUtilities.ResolveSelector(baseDocument, idSelector);
                    if (string.Equals(ChangeFingerprintUtilities.PathFor(selected), origin.Path, StringComparison.Ordinal))
                    {
                        return idSelector;
                    }
                }
                catch (InvalidOperationException)
                {
                }
            }

            return new NodeSelector(null, origin.Path);
        }

        private static void RecordReferences(
            VizzyProgramDocument working,
            XElement subtree,
            IDictionary<string, DeclarationFingerprint> declarations,
            ISet<string>? sameOperationDeclarations = null)
        {
            foreach (var element in subtree.DescendantsAndSelf())
            {
                var variableName = element.Attribute("variableName")?.Value;
                if (variableName != null &&
                    !string.Equals(element.Attribute("local")?.Value, "true", StringComparison.Ordinal))
                {
                    if (sameOperationDeclarations == null ||
                        !sameOperationDeclarations.Contains(DeclarationKey(DeclarationKind.Variable, variableName)))
                    {
                        RecordDeclaration(working, DeclarationKind.Variable, variableName, declarations);
                    }
                }

                var customNodeName = element.Attribute("customNodeName")?.Value;
                if (customNodeName != null)
                {
                    if (sameOperationDeclarations == null ||
                        !sameOperationDeclarations.Contains(DeclarationKey(DeclarationKind.CustomNode, customNodeName)))
                    {
                        RecordDeclaration(working, DeclarationKind.CustomNode, customNodeName, declarations);
                    }
                }
            }
        }

        private static ISet<string> GetSameOperationDeclarations(
            VizzyProgramDocument working,
            PatchOperation operation)
        {
            var declarations = new HashSet<string>(StringComparer.Ordinal);
            if (operation.Type != PatchOperationType.ReplaceNode || operation.Node == null)
            {
                return declarations;
            }

            var target = ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!);
            var parent = target.Parent;
            if (parent != null &&
                parent.Parent != null &&
                ReferenceEquals(parent.Parent, working.Root) &&
                string.Equals(parent.Name.LocalName, "Expressions", StringComparison.Ordinal) &&
                parent.Name.NamespaceName.Length == 0 &&
                string.Equals(operation.Node.Element, "CustomNode", StringComparison.Ordinal) &&
                operation.Node.Attributes.TryGetValue("name", out var customNodeName) &&
                !string.IsNullOrEmpty(customNodeName))
            {
                declarations.Add(DeclarationKey(DeclarationKind.CustomNode, customNodeName));
            }

            return declarations;
        }

        private static void RecordDeclaration(
            VizzyProgramDocument working,
            DeclarationKind kind,
            string name,
            IDictionary<string, DeclarationFingerprint> declarations)
        {
            var declaration = ChangeFingerprintUtilities.ResolveDeclaration(working, kind, name);
            var origin = declaration.Annotation<BaseOrigin>();
            if (origin == null)
            {
                return;
            }

            var baseName = origin.Snapshot.Attribute("name")?.Value
                ?? throw new InvalidOperationException("A base declaration is missing its name.");
            var key = DeclarationKey(kind, baseName);
            if (!declarations.ContainsKey(key))
            {
                declarations.Add(
                    key,
                    new DeclarationFingerprint(kind, baseName, ChangeFingerprintUtilities.ComputeHash(origin.Snapshot)));
            }
        }

        private static string DeclarationKey(DeclarationKind kind, string name)
        {
            return ((int)kind).ToString(CultureInfo.InvariantCulture) + ":" + name;
        }

        private static void ApplyOperation(VizzyProgramDocument working, PatchOperation operation)
        {
            switch (operation.Type)
            {
                case PatchOperationType.AddVariable:
                    GetContainer(working, "Variables").Add(
                        new XElement(
                            "Variable",
                            new XAttribute("name", operation.Name!),
                            new XAttribute("number", operation.Value ?? "0")));
                    break;
                case PatchOperationType.RenameVariable:
                    var renamed = ChangeFingerprintUtilities.ResolveDeclaration(
                        working,
                        DeclarationKind.Variable,
                        operation.Name!);
                    renamed.SetAttributeValue("name", operation.NewName!);
                    foreach (var reference in working.Root.DescendantsAndSelf()
                        .Select(element => element.Attribute("variableName"))
                        .Where(attribute =>
                            attribute != null &&
                            string.Equals(attribute.Value, operation.Name, StringComparison.Ordinal)))
                    {
                        reference!.Value = operation.NewName!;
                    }

                    break;
                case PatchOperationType.RemoveVariable:
                    ChangeFingerprintUtilities.ResolveDeclaration(
                        working,
                        DeclarationKind.Variable,
                        operation.Name!).Remove();
                    break;
                case PatchOperationType.InsertBefore:
                    ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!)
                        .AddBeforeSelf(operation.Node!.ToXElement());
                    break;
                case PatchOperationType.InsertAfter:
                    ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!)
                        .AddAfterSelf(operation.Node!.ToXElement());
                    break;
                case PatchOperationType.InsertChild:
                    ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!)
                        .Add(operation.Node!.ToXElement());
                    break;
                case PatchOperationType.ReplaceNode:
                    var replaced = ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!);
                    var replacement = operation.Node!.ToXElement();
                    if (replacement.Attribute("pos") == null && replaced.Attribute("pos") is XAttribute position)
                    {
                        replacement.Add(new XAttribute(position));
                    }

                    replaced.ReplaceWith(replacement);
                    break;
                case PatchOperationType.RemoveNode:
                    ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!).Remove();
                    break;
                case PatchOperationType.MoveNode:
                    var moved = ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!);
                    var destination = ChangeFingerprintUtilities.ResolveSelector(working, operation.Destination!);
                    moved.Remove();
                    destination.Add(moved);
                    break;
                case PatchOperationType.UpdateAttribute:
                    ChangeFingerprintUtilities.ResolveSelector(working, operation.Target!)
                        .SetAttributeValue(operation.Attribute!, operation.Value!);
                    break;
                default:
                    throw new InvalidOperationException("Unknown patch operation type.");
            }
        }

        private static XElement GetContainer(VizzyProgramDocument document, string name)
        {
            var matches = document.Root.Elements()
                .Where(element =>
                    element.Name.NamespaceName.Length == 0 &&
                    string.Equals(element.Name.LocalName, name, StringComparison.Ordinal))
                .Take(2)
                .ToList();
            if (matches.Count != 1)
            {
                throw new InvalidOperationException(name + " must resolve exactly once.");
            }

            return matches[0];
        }

        private sealed class BaseOrigin
        {
            public BaseOrigin(XElement snapshot, string path)
            {
                Snapshot = snapshot;
                Path = path;
            }

            public XElement Snapshot { get; }

            public string Path { get; }
        }
    }

    internal static class ChangeFingerprintUtilities
    {
        public static XElement ResolveSelector(VizzyProgramDocument document, NodeSelector selector)
        {
            if (selector.Id.HasValue)
            {
                var matches = document.Root.DescendantsAndSelf()
                    .Where(element => HasId(element, selector.Id.Value))
                    .Take(2)
                    .ToList();
                if (matches.Count != 1)
                {
                    throw new InvalidOperationException("A recorded id selector must resolve exactly once.");
                }

                return matches[0];
            }

            return document.FindByPath(selector.Path!)
                ?? throw new InvalidOperationException("A recorded path selector did not resolve.");
        }

        public static XElement ResolveDeclaration(
            VizzyProgramDocument document,
            DeclarationKind kind,
            string name)
        {
            var containerName = kind == DeclarationKind.Variable ? "Variables" : "Expressions";
            var declarationName = kind == DeclarationKind.Variable ? "Variable" : "CustomNode";
            var containers = document.Root.Elements()
                .Where(element => HasUnqualifiedName(element, containerName))
                .Take(2)
                .ToList();
            if (containers.Count != 1)
            {
                throw new InvalidOperationException(containerName + " must resolve exactly once.");
            }

            var declarations = containers[0].Elements()
                .Where(element =>
                    HasUnqualifiedName(element, declarationName) &&
                    string.Equals(element.Attribute("name")?.Value, name, StringComparison.Ordinal))
                .Take(2)
                .ToList();
            if (declarations.Count != 1)
            {
                throw new InvalidOperationException(
                    declarationName + " declaration '" + name + "' must resolve exactly once.");
            }

            return declarations[0];
        }

        public static string ComputeHash(XElement element)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(CanonicalXml.Write(element)));
            var result = new StringBuilder(bytes.Length * 2);
            foreach (var value in bytes)
            {
                result.Append(value.ToString("x2", CultureInfo.InvariantCulture));
            }

            return result.ToString();
        }

        public static string PathFor(XElement element)
        {
            var segments = element.AncestorsAndSelf()
                .Reverse()
                .Select(current =>
                {
                    var index = current.ElementsBeforeSelf().Count(sibling => sibling.Name == current.Name);
                    return current.Name.LocalName + "[" + index.ToString(CultureInfo.InvariantCulture) + "]";
                });
            return "/" + string.Join("/", segments);
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
            return element.Name.NamespaceName.Length == 0 &&
                string.Equals(element.Name.LocalName, name, StringComparison.Ordinal);
        }
    }
}
