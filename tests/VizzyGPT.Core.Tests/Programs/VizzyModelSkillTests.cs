using System;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Tests.Programs
{
    public sealed class VizzyModelSkillTests
    {
        private const string Toolbox =
            "<VizzyToolbox><Styles>" +
            "<Style id='set-input' color='CraftInstruction' />" +
            "<Style id='constant' color='Expression' />" +
            "</Styles><Categories><Category name='Craft Instructions'>" +
            "<SetInput style='set-input' input='throttle'><Constant number='0' /></SetInput>" +
            "<SetInput style='set-input' input='throttle'><Constant number='0' /></SetInput>" +
            "</Category></Categories></VizzyToolbox>";

        [Test]
        public void Catalog_retains_duplicate_collapsed_canonical_templates()
        {
            var catalog = VizzyNodeCatalog.FromToolboxXml(Toolbox);

            Assert.That(catalog.Templates, Has.Count.EqualTo(1));
            Assert.That(catalog.Templates.Single(), Is.EqualTo("<SetInput input=\"throttle\" style=\"set-input\"><Constant number=\"0\" /></SetInput>"));
            Assert.That(catalog.Templates.Single(), Does.Not.Contain("<Style"));
            Assert.That(VizzyNodeCatalog.FromToolboxXml(Toolbox).Templates, Is.EqualTo(catalog.Templates));
        }

        [Test]
        public void Catalog_uses_compact_instruction_and_expression_nodes_as_fallback_templates()
        {
            var catalog = VizzyNodeCatalog.FromToolboxXml(
                "<VizzyToolbox><Instructions><Wait style='wait'><Constant number='1' /></Wait></Instructions><Expressions><Constant number='0' /></Expressions></VizzyToolbox>");

            Assert.That(catalog.Templates, Is.EqualTo(new[]
            {
                "<Constant number=\"0\" />",
                "<Wait style=\"wait\"><Constant number=\"1\" /></Wait>"
            }));
        }

        [Test]
        public void Modify_reference_contains_semantic_rules_and_complete_templates_within_its_budget()
        {
            var reference = VizzyModelSkill.BuildModifyReference(VizzyNodeCatalog.FromToolboxXml(Toolbox));

            Assert.That(reference, Does.Contain("- Events start cooperative instruction sequences."));
            Assert.That(reference, Does.Contain("- Craft controls use SetInput; do not invent dedicated throttle/pitch/roll nodes."));
            Assert.That(reference, Does.Contain("Prefer id selectors from the current program XML"));
            Assert.That(reference, Does.Contain("/Program[0]/Instructions[0]/Event[0]"));
            Assert.That(reference, Does.Contain("every path segment must include [index]"));
            Assert.That(reference, Does.Contain("Use only listed element/style combinations"));
            Assert.That(reference, Does.Contain("<SetInput input=\"throttle\" style=\"set-input\"><Constant number=\"0\" /></SetInput>"));
            Assert.That(reference.Length, Is.LessThanOrEqualTo(VizzyModelSkill.MaximumReferenceCharacters));
        }

        [Test]
        public void Modify_reference_truncates_only_between_complete_template_lines()
        {
            var reference = VizzyModelSkill.BuildModifyReference(VizzyNodeCatalog.FromToolboxXml(LargeToolbox()));

            Assert.That(reference.Length, Is.LessThanOrEqualTo(VizzyModelSkill.MaximumReferenceCharacters));
            Assert.That(reference, Does.EndWith("[CATALOG TRUNCATED]"));
            Assert.That(reference.Split(new[] { Environment.NewLine }, StringSplitOptions.None)
                .Where(line => line.StartsWith("<SetInput", StringComparison.Ordinal))
                .All(line => line.EndsWith("</SetInput>", StringComparison.Ordinal)), Is.True);
        }

        [Test]
        public void Repair_reference_selects_the_valid_throttle_template_from_punctuated_input()
        {
            var reference = VizzyModelSkill.BuildRepairReference(
                VizzyNodeCatalog.FromToolboxXml(Toolbox),
                "<SetThrottle />",
                "/Program/Instructions/set-throttle",
                "Unknown element: set-throttle.");

            Assert.That(reference, Does.Contain("<SetInput input=\"throttle\" style=\"set-input\"><Constant number=\"0\" /></SetInput>"));
            Assert.That(reference.Length, Is.LessThanOrEqualTo(4000));
        }

        [Test]
        public void Repair_reference_falls_back_to_a_bounded_complete_reference_when_nothing_matches()
        {
            var reference = VizzyModelSkill.BuildRepairReference(
                VizzyNodeCatalog.FromToolboxXml(Toolbox),
                "???",
                null,
                "bad");

            Assert.That(reference, Does.Contain("VIZZY MODEL SKILL"));
            Assert.That(reference, Does.Contain("<SetInput input=\"throttle\" style=\"set-input\"><Constant number=\"0\" /></SetInput>"));
            Assert.That(reference.Length, Is.LessThanOrEqualTo(4000));
        }

        private static string LargeToolbox()
        {
            var nodes = new StringBuilder();
            for (var index = 0; index < 1000; index++)
            {
                nodes.Append("<SetInput style='set-input' input='channel");
                nodes.Append(index);
                nodes.Append("'><Constant number='0' /></SetInput>");
            }

            return "<VizzyToolbox><Styles><Style id='set-input' color='CraftInstruction' /></Styles><Categories><Category name='Craft Instructions'>" +
                nodes +
                "</Category></Categories></VizzyToolbox>";
        }
    }
}
