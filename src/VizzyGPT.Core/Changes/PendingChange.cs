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
            var targetFingerprints = new List<TargetFingerprint>();
            var variableNames = new HashSet<string>(StringComparer.Ordinal);
            var customNodeNames = new HashSet<string>(StringComparer.Ordinal);

            foreach (var operation in session.Patch.Operations)
            {
                AddSelectorFingerprint(
                    baseDocument,
                    operation.Target,
                    targetFingerprints,
                    variableNames,
                    customNodeNames);
                AddSelectorFingerprint(
                    baseDocument,
                    operation.Destination,
                    targetFingerprints,
                    variableNames,
                    customNodeNames);

                if (operation.Node != null)
                {
                    ChangeFingerprintUtilities.CollectDeclarationReferences(
                        operation.Node.ToXElement(),
                        variableNames,
                        customNodeNames);
                }
            }

            var declarations = new List<DeclarationFingerprint>();
            foreach (var variableName in variableNames.OrderBy(name => name, StringComparer.Ordinal))
            {
                var declaration = ChangeFingerprintUtilities.ResolveDeclaration(
                    baseDocument,
                    DeclarationKind.Variable,
                    variableName);
                declarations.Add(
                    new DeclarationFingerprint(
                        DeclarationKind.Variable,
                        variableName,
                        ChangeFingerprintUtilities.ComputeHash(declaration)));
            }

            foreach (var customNodeName in customNodeNames.OrderBy(name => name, StringComparer.Ordinal))
            {
                var declaration = ChangeFingerprintUtilities.ResolveDeclaration(
                    baseDocument,
                    DeclarationKind.CustomNode,
                    customNodeName);
                declarations.Add(
                    new DeclarationFingerprint(
                        DeclarationKind.CustomNode,
                        customNodeName,
                        ChangeFingerprintUtilities.ComputeHash(declaration)));
            }

            return new PendingChange(
                programFingerprint,
                session.BaseXml,
                session.BaseHash,
                JsonConvert.SerializeObject(session.Patch, Formatting.None),
                session.ResultXml,
                session.ResultHash,
                targetFingerprints,
                declarations,
                createdUtc,
                session.PreviewLines);
        }

        private static void AddSelectorFingerprint(
            VizzyProgramDocument document,
            NodeSelector? selector,
            ICollection<TargetFingerprint> fingerprints,
            ISet<string> variableNames,
            ISet<string> customNodeNames)
        {
            if (selector == null)
            {
                return;
            }

            var selected = ChangeFingerprintUtilities.ResolveSelector(document, selector);
            fingerprints.Add(
                new TargetFingerprint(selector, ChangeFingerprintUtilities.ComputeHash(selected)));
            ChangeFingerprintUtilities.CollectDeclarationReferences(selected, variableNames, customNodeNames);
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

        public static void CollectDeclarationReferences(
            XElement subtree,
            ISet<string> variableNames,
            ISet<string> customNodeNames)
        {
            foreach (var element in subtree.DescendantsAndSelf())
            {
                var variableName = element.Attribute("variableName")?.Value;
                if (variableName != null &&
                    !string.Equals(element.Attribute("local")?.Value, "true", StringComparison.Ordinal))
                {
                    variableNames.Add(variableName);
                }

                var customNodeName = element.Attribute("customNodeName")?.Value;
                if (customNodeName != null)
                {
                    customNodeNames.Add(customNodeName);
                }
            }
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
