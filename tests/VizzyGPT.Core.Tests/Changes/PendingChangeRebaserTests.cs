using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Changes;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;

namespace VizzyGPT.Core.Tests.Changes
{
    public sealed class PendingChangeRebaserTests
    {
        [Test]
        public void ChangeSession_records_base_patch_result_hashes_and_preview_lines()
        {
            var baseDocument = BaseDocument();
            var patch = PatchFor(baseDocument);
            var patchResult = VizzyPatchEngine.Apply(baseDocument, patch);

            var session = ChangeSession.Create(baseDocument, patch, patchResult, ValidReport());

            Assert.That(session.BaseXml, Is.EqualTo(baseDocument.ToXml()));
            Assert.That(session.BaseHash, Is.EqualTo(VizzyProgramHash.Compute(baseDocument)));
            Assert.That(session.Patch, Is.SameAs(patch));
            Assert.That(session.ResultXml, Is.EqualTo(patchResult.Document.ToXml()));
            Assert.That(session.ResultHash, Is.EqualTo(VizzyProgramHash.Compute(patchResult.Document)));
            Assert.That(session.PreviewLines, Is.EqualTo(patchResult.Changes));
        }

        [Test]
        public void ChangeSession_refuses_an_invalid_validation_report()
        {
            var baseDocument = BaseDocument();
            var patch = PatchFor(baseDocument);
            var patchResult = VizzyPatchEngine.Apply(baseDocument, patch);
            var invalidReport = new ValidationReport(
                new[] { new ValidationIssue(ValidationSeverity.Error, "invalid", "Invalid result") });

            Assert.Throws<InvalidOperationException>(
                () => ChangeSession.Create(baseDocument, patch, patchResult, invalidReport));
        }

        [Test]
        public void PendingChange_persists_snapshots_patch_fingerprints_timestamp_and_preview()
        {
            var createdUtc = new DateTime(2026, 7, 21, 4, 5, 6, DateTimeKind.Utc);
            var session = SessionFor(BaseDocument());

            var pending = PendingChange.Create("program-alpha", session, createdUtc);

            Assert.That(pending.ProgramFingerprint, Is.EqualTo("program-alpha"));
            Assert.That(pending.BaseXml, Is.EqualTo(session.BaseXml));
            Assert.That(pending.BaseHash, Is.EqualTo(session.BaseHash));
            Assert.That(PatchDocument.Deserialize(pending.PatchJson).BaseHash, Is.EqualTo(session.Patch.BaseHash));
            Assert.That(pending.ResultXml, Is.EqualTo(session.ResultXml));
            Assert.That(pending.ResultHash, Is.EqualTo(session.ResultHash));
            Assert.That(pending.CreatedUtc, Is.EqualTo(createdUtc));
            Assert.That(pending.PreviewLines, Is.EqualTo(session.PreviewLines));

            Assert.That(pending.TargetFingerprints, Has.Count.EqualTo(1));
            Assert.That(pending.TargetFingerprints[0].Selector.Id, Is.EqualTo(1));
            Assert.That(pending.TargetFingerprints[0].Hash, Is.Not.Null.And.Not.Empty);

            Assert.That(pending.DeclarationFingerprints, Has.Count.EqualTo(2));
            AssertDeclaration(pending, DeclarationKind.Variable, "pitch");
            AssertDeclaration(pending, DeclarationKind.CustomNode, "guidance");
        }

        [Test]
        public void PendingChange_fingerprints_every_target_and_destination_selector()
        {
            var document = VizzyProgramDocument.Parse(
                "<Program><Variables /><Instructions><Log id='1' /><While id='2'><Instructions /></While></Instructions><Expressions /></Program>");
            var destinationPath = "/Program[0]/Instructions[0]/While[0]/Instructions[0]";
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Move the selected log",
                new[]
                {
                    new PatchOperation(
                        PatchOperationType.MoveNode,
                        target: new NodeSelector(1, null),
                        destination: new NodeSelector(null, destinationPath))
                });
            var patchResult = VizzyPatchEngine.Apply(document, patch);
            var session = ChangeSession.Create(document, patch, patchResult, ValidReport());

            var pending = PendingChange.Create(
                "program-selectors",
                session,
                new DateTime(2026, 7, 21, 4, 5, 6, DateTimeKind.Utc));

            Assert.That(pending.TargetFingerprints, Has.Count.EqualTo(2));
            Assert.That(pending.TargetFingerprints.Any(item => item.Selector.Id == 1), Is.True);
            Assert.That(
                pending.TargetFingerprints.Any(item => string.Equals(item.Selector.Path, destinationPath, StringComparison.Ordinal)),
                Is.True);
            Assert.That(pending.TargetFingerprints.All(item => !string.IsNullOrEmpty(item.Hash)), Is.True);
        }

        [TestCase(PatchOperationType.RenameVariable)]
        [TestCase(PatchOperationType.RemoveVariable)]
        public void Named_variable_operations_fingerprint_the_base_declaration_and_conflict_after_it_changes(
            PatchOperationType operationType)
        {
            var document = VariableOperationDocument();
            var operation = operationType == PatchOperationType.RenameVariable
                ? new PatchOperation(operationType, name: "pitch", newName: "yaw")
                : new PatchOperation(operationType, name: "pitch");

            var pending = CreatePending(document, operation);

            AssertDeclaration(pending, DeclarationKind.Variable, "pitch");

            var current = VariableOperationDocument();
            current.Root.Element("Variables")!.Element("Variable")!.SetAttributeValue("number", "9");
            AssertConflict(pending, current);
        }

        [Test]
        public void Update_variableName_fingerprints_the_referenced_base_declaration_and_conflicts_after_it_changes()
        {
            var document = VariableAttributeDocument();
            var operation = new PatchOperation(
                PatchOperationType.UpdateAttribute,
                target: new NodeSelector(2, null),
                attribute: "variableName",
                value: "pitch");

            var pending = CreatePending(document, operation);

            AssertDeclaration(pending, DeclarationKind.Variable, "pitch");

            var current = VariableAttributeDocument();
            current.Root.Element("Variables")!.Element("Variable")!.SetAttributeValue("number", "9");
            AssertConflict(pending, current);
        }

        [Test]
        public void PendingChange_accepts_update_of_an_id_introduced_by_an_earlier_operation()
        {
            var document = VariableOperationDocument();
            var inserted = new NodeSpec(
                "Log",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["id"] = "2",
                    ["text"] = "inserted"
                },
                Array.Empty<NodeSpec>());
            var pending = CreatePending(
                document,
                new PatchOperation(
                    PatchOperationType.InsertAfter,
                    target: new NodeSelector(1, null),
                    node: inserted),
                new PatchOperation(
                    PatchOperationType.UpdateAttribute,
                    target: new NodeSelector(2, null),
                    attribute: "text",
                    value: "updated"));

            Assert.That(pending.TargetFingerprints, Has.Count.EqualTo(1));
            Assert.That(pending.TargetFingerprints[0].Selector.Id, Is.EqualTo(1));
            Assert.That(pending.DeclarationFingerprints, Is.Empty);
            Assert.That(VizzyProgramDocument.Parse(pending.ResultXml).FindById(2)!.Attribute("text")!.Value, Is.EqualTo("updated"));
        }

        [Test]
        public void PendingChange_accepts_a_NodeSpec_reference_to_a_variable_added_by_an_earlier_operation()
        {
            var document = VariableOperationDocument();
            var inserted = new NodeSpec(
                "SetVariable",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["id"] = "2",
                },
                new[]
                {
                    new NodeSpec(
                        "Variable",
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["local"] = "false",
                            ["variableName"] = "yaw"
                        },
                        Array.Empty<NodeSpec>()),
                    new NodeSpec(
                        "Constant",
                        new Dictionary<string, string>(StringComparer.Ordinal) { ["number"] = "0" },
                        Array.Empty<NodeSpec>())
                });
            var pending = CreatePending(
                document,
                new PatchOperation(PatchOperationType.AddVariable, name: "yaw"),
                new PatchOperation(
                    PatchOperationType.InsertAfter,
                    target: new NodeSelector(1, null),
                    node: inserted));

            Assert.That(pending.TargetFingerprints, Has.Count.EqualTo(1));
            Assert.That(pending.TargetFingerprints[0].Selector.Id, Is.EqualTo(1));
            Assert.That(pending.DeclarationFingerprints, Is.Empty);
            var result = VizzyProgramDocument.Parse(pending.ResultXml);
            Assert.That(result.Root.Element("Variables")!.Elements("Variable").Any(item => item.Attribute("name")?.Value == "yaw"), Is.True);
            Assert.That(result.FindById(2)!.Descendants("Variable").Single().Attribute("variableName")!.Value, Is.EqualTo("yaw"));
        }

        [Test]
        public void TryRebase_returns_unchanged_with_the_stored_result_when_hashes_match()
        {
            var current = BaseDocument();
            var pending = PendingFor(BaseDocument());

            var result = new PendingChangeRebaser().TryRebase(pending, current);

            Assert.That(result.Status, Is.EqualTo(RebaseStatus.Unchanged));
            Assert.That(result.Patch, Is.Not.Null);
            Assert.That(result.Patch!.BaseHash, Is.EqualTo(pending.BaseHash));
            Assert.That(result.Document, Is.Not.Null);
            Assert.That(result.Document!.ToXml(), Is.EqualTo(pending.ResultXml));
        }

        [Test]
        public void TryRebase_reapplies_after_an_unrelated_edit_and_changes_only_patch_baseHash()
        {
            var pending = PendingFor(BaseDocument());
            var current = BaseDocument();
            current.Root.SetAttributeValue("name", "User edit outside selected subtrees");

            var result = new PendingChangeRebaser().TryRebase(pending, current);

            Assert.That(result.Status, Is.EqualTo(RebaseStatus.Rebased));
            Assert.That(result.Patch, Is.Not.Null);
            Assert.That(result.Patch!.BaseHash, Is.EqualTo(VizzyProgramHash.Compute(current)));
            AssertPatchDiffersOnlyByBaseHash(pending.PatchJson, result.Patch);
            Assert.That(result.Document, Is.Not.Null);
            Assert.That(result.Document!.Root.Attribute("name")!.Value, Is.EqualTo("User edit outside selected subtrees"));
            Assert.That(result.Document.FindById(1)!.Name.LocalName, Is.EqualTo("Compute"));
            Assert.That(result.Document.FindById(1)!.Attribute("text")!.Value, Is.EqualTo("after"));
            Assert.That(PatchDocument.Deserialize(pending.PatchJson).BaseHash, Is.EqualTo(pending.BaseHash));
        }

        [Test]
        public void TryRebase_returns_conflict_without_a_document_when_a_selected_target_changed()
        {
            var pending = PendingFor(BaseDocument());
            var current = BaseDocument();
            current.FindById(1)!.SetAttributeValue("text", "user changed target");

            AssertConflict(pending, current);
        }

        [Test]
        public void TryRebase_returns_conflict_when_an_id_selector_becomes_ambiguous()
        {
            var pending = PendingFor(BaseDocument());
            var current = BaseDocument();
            current.Root.Element("Instructions")!.Add(new XElement("Log", new XAttribute("id", "1")));

            AssertConflict(pending, current);
        }

        [Test]
        public void TryRebase_returns_conflict_when_a_referenced_variable_declaration_changed()
        {
            var pending = PendingFor(BaseDocument());
            var current = BaseDocument();
            current.Root.Element("Variables")!.Element("Variable")!.SetAttributeValue("number", "9");

            AssertConflict(pending, current);
        }

        [Test]
        public void TryRebase_returns_conflict_when_a_referenced_custom_node_declaration_changed()
        {
            var pending = PendingFor(BaseDocument());
            var current = BaseDocument();
            current.Root.Element("Expressions")!
                .Element("CustomNode")!
                .Element("Constant")!
                .SetAttributeValue("number", "2");

            AssertConflict(pending, current);
        }

        private static void AssertDeclaration(PendingChange pending, DeclarationKind kind, string name)
        {
            var fingerprint = pending.DeclarationFingerprints.Single(
                candidate => candidate.Kind == kind && string.Equals(candidate.Name, name, StringComparison.Ordinal));
            Assert.That(fingerprint.Hash, Is.Not.Null.And.Not.Empty);
        }

        private static void AssertConflict(PendingChange pending, VizzyProgramDocument current)
        {
            var currentXml = current.ToXml();

            var result = new PendingChangeRebaser().TryRebase(pending, current);

            Assert.That(result.Status, Is.EqualTo(RebaseStatus.Conflict));
            Assert.That(result.Patch, Is.Null);
            Assert.That(result.Document, Is.Null);
            Assert.That(current.ToXml(), Is.EqualTo(currentXml));
        }

        private static void AssertPatchDiffersOnlyByBaseHash(string originalJson, PatchDocument rebasedPatch)
        {
            var original = JObject.Parse(originalJson);
            var rebased = JObject.FromObject(rebasedPatch);
            rebased["baseHash"] = original["baseHash"]!.DeepClone();

            Assert.That(JToken.DeepEquals(rebased, original), Is.True);
        }

        private static PendingChange PendingFor(VizzyProgramDocument document) =>
            PendingChange.Create(
                "program-alpha",
                SessionFor(document),
                new DateTime(2026, 7, 21, 4, 5, 6, DateTimeKind.Utc));

        private static PendingChange CreatePending(
            VizzyProgramDocument document,
            params PatchOperation[] operations)
        {
            var patch = new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Review regression patch",
                operations);
            var result = VizzyPatchEngine.Apply(document, patch);
            var session = ChangeSession.Create(document, patch, result, ValidReport());
            return PendingChange.Create(
                "program-review",
                session,
                new DateTime(2026, 7, 21, 5, 0, 0, DateTimeKind.Utc));
        }

        private static ChangeSession SessionFor(VizzyProgramDocument document)
        {
            var patch = PatchFor(document);
            var result = VizzyPatchEngine.Apply(document, patch);
            return ChangeSession.Create(document, patch, result, ValidReport());
        }

        private static ValidationReport ValidReport() =>
            new ValidationReport(Array.Empty<ValidationIssue>());

        private static PatchDocument PatchFor(VizzyProgramDocument document)
        {
            var replacement = new NodeSpec(
                "Compute",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["id"] = "1",
                    ["text"] = "after",
                    ["variableName"] = "pitch",
                    ["customNodeName"] = "guidance"
                },
                Array.Empty<NodeSpec>());
            var operation = new PatchOperation(
                PatchOperationType.ReplaceNode,
                target: new NodeSelector(1, null),
                node: replacement);
            return new PatchDocument(
                VizzyProgramHash.Compute(document),
                "Replace the selected computation",
                new[] { operation });
        }

        private static VizzyProgramDocument BaseDocument() => VizzyProgramDocument.Parse(
            "<Program>" +
            "<Variables><Variable name='pitch' number='0' /></Variables>" +
            "<Instructions><Log id='1' text='before' /></Instructions>" +
            "<Expressions><CustomNode name='guidance'><Constant number='1' /></CustomNode></Expressions>" +
            "</Program>");

        private static VizzyProgramDocument VariableOperationDocument() => VizzyProgramDocument.Parse(
            "<Program><Variables><Variable name='pitch' number='0' /></Variables>" +
            "<Instructions><Log id='1' text='before' /></Instructions><Expressions /></Program>");

        private static VizzyProgramDocument VariableAttributeDocument() => VizzyProgramDocument.Parse(
            "<Program><Variables><Variable name='pitch' number='0' /></Variables>" +
            "<Instructions><SetVariable id='1'><Variable id='2' local='false' />" +
            "<Constant number='0' /></SetVariable></Instructions><Expressions /></Program>");
    }
}
