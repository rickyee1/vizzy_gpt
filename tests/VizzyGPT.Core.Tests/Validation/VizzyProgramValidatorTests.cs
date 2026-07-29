using System;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Validation;

namespace VizzyGPT.Core.Tests.Validation
{
    public sealed class VizzyProgramValidatorTests
    {
        [TestCase("Variables")]
        [TestCase("Instructions")]
        [TestCase("Expressions")]
        public void Validate_reports_each_missing_required_container(string containerName)
        {
            var root = XElement.Parse(ValidProgramXml);
            root.Element(containerName)!.Remove();

            var report = Validator().Validate(Document(root), Catalog());

            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Errors.Select(issue => issue.Code), Does.Contain("MissingContainer"));
            Assert.That(report.Errors.Single(issue => issue.Code == "MissingContainer").Message, Does.Contain(containerName));
        }

        [Test]
        public void Validate_reports_duplicate_ids()
        {
            var document = Document(
                "<Program><Variables /><Instructions><Event id='7' style='flight-start' /><Log id='7' style='log' /></Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "DuplicateId", "7");
        }

        [TestCase("not-a-number")]
        [TestCase("2147483648")]
        [TestCase("-2147483649")]
        public void Validate_reports_ids_that_are_not_Int32_values(string id)
        {
            var document = Document(
                "<Program><Variables /><Instructions><Event id='" + id + "' style='flight-start' /></Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "InvalidId", id);
        }

        [Test]
        public void Validate_reports_styles_absent_from_the_ordinal_catalog()
        {
            var document = Document(
                "<Program><Variables /><Instructions><Event id='1' style='FLIGHT-START' /></Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "UnknownStyle", "FLIGHT-START");
        }

        [Test]
        public void Validate_reports_unresolved_global_variable_references()
        {
            var document = Document(
                "<Program><Variables><Variable name='pitch' number='0' /></Variables><Instructions><SetVariable id='1' style='set-variable'><Variable local='false' variableName='Pitch' /></SetVariable></Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "UnresolvedVariable", "Pitch");
        }

        [TestCase("<Constant />")]
        [TestCase("<Constant number='not-a-number' />")]
        [TestCase("<Constant bool='yes' />")]
        [TestCase("<Constant text='one' number='1' />")]
        public void Validate_reports_malformed_constants(string constantXml)
        {
            var document = Document(
                "<Program><Variables /><Instructions /><Expressions>" + constantXml + "</Expressions></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "MalformedConstant", "Constant");
        }

        [Test]
        public void Validate_reports_expression_nodes_placed_in_instruction_containers()
        {
            var document = Document(
                "<Program><Variables /><Instructions><Constant number='1' /></Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "InvalidChildPlacement", "Constant");
        }

        [Test]
        public void Validate_reports_a_catalog_instruction_placed_directly_under_root_Expressions()
        {
            var document = Document(
                "<Program><Variables /><Instructions /><Expressions><DynamicInstruction id='1' /></Expressions></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "InvalidChildPlacement", "DynamicInstruction");
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Validate_reports_a_catalog_expression_placed_directly_under_any_Instructions_container(
            bool nested)
        {
            var instructions = nested
                ? "<While id='1'><Instructions><DynamicExpression id='2' /></Instructions></While>"
                : "<DynamicExpression id='2' />";
            var document = Document(
                "<Program><Variables /><Instructions>" + instructions + "</Instructions><Expressions /></Program>");

            var report = Validator().Validate(document, Catalog());

            AssertError(report, "InvalidChildPlacement", "DynamicExpression");
        }

        [TestCase("Instructions", "Variables")]
        [TestCase("Instructions", "Expressions")]
        [TestCase("Expressions", "Instructions")]
        [TestCase("Variables", "Instructions")]
        public void Validate_reports_structural_containers_in_invalid_direct_parent_positions(
            string parentName,
            string childName)
        {
            var root = XElement.Parse(ValidProgramXml);
            root.Element(parentName)!.Add(new XElement(childName));

            var report = Validator().Validate(Document(root), Catalog());

            AssertError(report, "InvalidChildPlacement", childName);
        }

        [Test]
        public void Validate_accepts_nested_instructions_with_a_stock_shaped_toolbox_catalog()
        {
            var document = Document(
                "<Program><Variables /><Instructions>" +
                "<Event id='1' event='FlightStart' style='flight-start'>" +
                "<Instructions><LogMessage id='2' style='log' /></Instructions>" +
                "</Event></Instructions><Expressions /></Program>");
            var catalog = VizzyNodeCatalog.FromToolboxXml(
                "<VizzyToolbox>" +
                "<Colors><Color id='Event' /><Color id='Instruction' /></Colors>" +
                "<Styles><Style id='flight-start' color='Event' /><Style id='log' color='Instruction' /></Styles>" +
                "<Categories><Category name='Events'><Event style='flight-start' /></Category>" +
                "<Category name='Program Flow'><LogMessage style='log' /></Category></Categories>" +
                "</VizzyToolbox>");

            var report = Validator().Validate(document, catalog);

            Assert.That(report.IsValid, Is.True, string.Join("\n", report.Errors.Select(issue => issue.Message)));
        }

        [Test]
        public void Validate_passes_canonical_xml_to_a_successful_runtime_serializer_callback()
        {
            string? serializedXml = null;
            var validator = new VizzyProgramValidator(
                xml =>
                {
                    serializedXml = xml;
                    return null;
                });
            var document = Document(ValidProgramXml);

            var report = validator.Validate(document, Catalog());

            Assert.That(report.IsValid, Is.True);
            Assert.That(report.Issues, Is.Empty);
            Assert.That(serializedXml, Is.EqualTo(document.ToXml()));
        }

        [Test]
        public void ValidationReport_exposes_ordered_issues_and_separate_errors_and_warnings()
        {
            var warning = new ValidationIssue(ValidationSeverity.Warning, "warning", "Warning message", "/Program[0]");
            var error = new ValidationIssue(ValidationSeverity.Error, "error", "Error message", "/Program[0]/Instructions[0]");

            var report = new ValidationReport(new[] { warning, error });
            var warningOnly = new ValidationReport(new[] { warning });

            Assert.That(report.IsValid, Is.False);
            Assert.That(report.Issues, Is.EqualTo(new[] { warning, error }));
            Assert.That(report.Errors, Is.EqualTo(new[] { error }));
            Assert.That(report.Warnings, Is.EqualTo(new[] { warning }));
            Assert.That(warningOnly.IsValid, Is.True);
        }

        [Test]
        public void Validate_orders_pure_xml_then_catalog_then_runtime_issues_deterministically()
        {
            var root = XElement.Parse(ValidProgramXml);
            root.Element("Variables")!.Remove();
            root.Element("Instructions")!.Element("Event")!.SetAttributeValue("style", "unknown-style");
            var runtimeIssue = new ValidationIssue(ValidationSeverity.Warning, "RuntimeSerializer", "Runtime warning");
            var validator = new VizzyProgramValidator(_ => runtimeIssue);

            var report = validator.Validate(Document(root), Catalog());

            Assert.That(
                report.Issues.Select(issue => issue.Code),
                Is.EqualTo(new[] { "MissingContainer", "UnknownStyle", "RuntimeSerializer" }));
            Assert.That(report.Errors.Select(issue => issue.Code), Is.EqualTo(new[] { "MissingContainer", "UnknownStyle" }));
            Assert.That(report.Warnings, Is.EqualTo(new[] { runtimeIssue }));
        }

        [Test]
        public void Validate_keeps_new_id_and_placement_errors_before_catalog_and_runtime_issues()
        {
            var document = Document(
                "<Program><Variables /><Instructions><DynamicExpression id='invalid' style='unknown-style' /></Instructions><Expressions /></Program>");
            var runtimeIssue = new ValidationIssue(ValidationSeverity.Warning, "RuntimeSerializer", "Runtime warning");

            var report = new VizzyProgramValidator(_ => runtimeIssue).Validate(document, Catalog());

            Assert.That(
                report.Issues.Select(issue => issue.Code),
                Is.EqualTo(new[] { "InvalidId", "InvalidChildPlacement", "UnknownStyle", "RuntimeSerializer" }));
        }

        private const string ValidProgramXml =
            "<Program><Variables><Variable name='pitch' number='0' /></Variables>" +
            "<Instructions><Event id='1' event='FlightStart' style='flight-start' /></Instructions>" +
            "<Expressions /></Program>";

        private static VizzyProgramValidator Validator() => new VizzyProgramValidator(_ => null);

        private static VizzyNodeCatalog Catalog() => VizzyNodeCatalog.FromToolboxXml(
            "<VizzyToolbox><Styles>" +
            "<Style id='flight-start' /><Style id='log' /><Style id='set-variable' />" +
            "</Styles><Instructions><Event /><Log /><SetVariable /><While /><DynamicInstruction /></Instructions>" +
            "<Expressions><Constant /><Variable /><CustomNode /><DynamicExpression /></Expressions></VizzyToolbox>");

        private static VizzyProgramDocument Document(string xml) => VizzyProgramDocument.Parse(xml);

        private static VizzyProgramDocument Document(XElement root) =>
            Document(root.ToString(SaveOptions.DisableFormatting));

        private static void AssertError(ValidationReport report, string code, string messageFragment)
        {
            Assert.That(report.IsValid, Is.False);
            var issue = report.Errors.Single(candidate => candidate.Code == code);
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Message, Does.Contain(messageFragment));
        }
    }
}
