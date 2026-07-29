using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Tests.Programs
{
public sealed class CanonicalXmlTests
{
    [Test]
    public void Attribute_order_does_not_change_hash()
    {
        var a = VizzyProgramDocument.Parse("<Program name='A'><Variables/><Instructions><Event id='0' style='flight-start' event='FlightStart'/></Instructions><Expressions/></Program>");
        var b = VizzyProgramDocument.Parse("<Program name='A'><Variables/><Instructions><Event event='FlightStart' style='flight-start' id='0'/></Instructions><Expressions/></Program>");

        Assert.That(VizzyProgramHash.Compute(a), Is.EqualTo(VizzyProgramHash.Compute(b)));
    }

    [Test]
    public void Clone_is_independent_and_ids_are_queryable()
    {
        var original = VizzyProgramDocument.Parse(ReadFixture("minimal.xml"));
        var clone = original.Clone();

        clone.FindById(0)!.SetAttributeValue("event", "ReceiveMessage");

        Assert.That(original.FindById(0)!.Attribute("event")!.Value, Is.EqualTo("FlightStart"));
    }

    [Test]
    public void Parse_rejects_non_program_root()
    {
        var exception = Assert.Throws<ArgumentException>(() => VizzyProgramDocument.Parse("<NotProgram />"));

        Assert.That(exception!.ParamName, Is.EqualTo("xml"));
    }

    [Test]
    public void FindByPath_uses_deterministic_indexed_paths()
    {
        var document = VizzyProgramDocument.Parse(ReadFixture("minimal.xml"));

        var node = document.FindByPath("/Program[0]/Instructions[0]/Event[0]");

        Assert.That(node!.Attribute("id")!.Value, Is.EqualTo("0"));
    }

    [TestCase("Program[0]/Instructions[0]/Event[0]")]
    [TestCase("//Program[0]/Instructions[0]/Event[0]")]
    [TestCase("/Program[0]/Instructions[0]/Event[0]/")]
    [TestCase("/Program[0]/Instructions[0]/Event[x]")]
    [TestCase("/Program[0]/Instructions[0]/Event[0][0]")]
    [TestCase("/Program[0]/Instructions[0]/Event[1]")]
    public void FindByPath_rejects_noncanonical_or_out_of_range_paths(string path)
    {
        var document = VizzyProgramDocument.Parse(ReadFixture("minimal.xml"));

        Assert.That(document.FindByPath(path), Is.Null);
    }

    [Test]
    public void FindByPath_selects_the_requested_sibling_index()
    {
        var document = VizzyProgramDocument.Parse("<Program><Instructions><Event id='0' /><Event id='1' /></Instructions></Program>");

        var node = document.FindByPath("/Program[0]/Instructions[0]/Event[1]");

        Assert.That(node!.Attribute("id")!.Value, Is.EqualTo("1"));
    }

    [Test]
    public void Canonical_xml_preserves_text_and_child_order()
    {
        var document = VizzyProgramDocument.Parse("<Program z='2' a='1'><First> text </First>  <Second /></Program>");

        Assert.That(document.ToXml(), Is.EqualTo("<Program a=\"1\" z=\"2\"><First> text </First><Second /></Program>"));
    }

    [Test]
    public void Canonical_xml_preserves_whitespace_in_mixed_content()
    {
        var document = VizzyProgramDocument.Parse("<Program><Message>before<Strong /> <Em />after</Message></Program>");

        Assert.That(document.ToXml(), Is.EqualTo("<Program><Message>before<Strong /> <Em />after</Message></Program>"));
    }

    [Test]
    public void Hash_is_known_lowercase_utf8_sha256()
    {
        var document = VizzyProgramDocument.Parse("<Program />");

        Assert.That(VizzyProgramHash.Compute(document), Is.EqualTo("4d71a37c5d882b37ba3151544effaa23aadde9a12df8ae02d13915ddedb14d00"));
    }

    [Test]
    public void Node_catalog_extracts_styles_and_element_names()
    {
        var catalog = VizzyNodeCatalog.FromToolboxXml("<VizzyToolbox><Styles><Style id='flight-start' /></Styles><Instructions><Event style='flight-start' /></Instructions></VizzyToolbox>");

        Assert.That(catalog.ContainsStyle("flight-start"), Is.True);
        Assert.That(catalog.ContainsElement("Event"), Is.True);
        Assert.That(catalog.ContainsStyle("FLIGHT-START"), Is.False);
        Assert.That(catalog.ContainsElement("Unknown"), Is.False);
    }

    [Test]
    public void Fixture_files_are_not_mutated_by_document_operations()
    {
        var minimalPath = FixturePath("minimal.xml");
        var nestedPath = FixturePath("nested.xml");
        var minimalBefore = File.ReadAllBytes(minimalPath);
        var nestedBefore = File.ReadAllBytes(nestedPath);

        _ = VizzyProgramDocument.Parse(File.ReadAllText(minimalPath)).Clone().ToXml();
        _ = VizzyProgramDocument.Parse(File.ReadAllText(nestedPath)).Clone().ToXml();

        Assert.That(File.ReadAllBytes(minimalPath), Is.EqualTo(minimalBefore));
        Assert.That(File.ReadAllBytes(nestedPath), Is.EqualTo(nestedBefore));
    }

    private static string ReadFixture(string name) => File.ReadAllText(FixturePath(name));

    private static string FixturePath(string name) => Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", name);
}
}
