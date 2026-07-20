using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;

namespace VizzyGPT.Core.Tests.Patching
{
    public sealed class VizzyPatchEngineTests
    {
        private static readonly string[] OperationTypes =
        {
            "addVariable",
            "renameVariable",
            "removeVariable",
            "insertBefore",
            "insertAfter",
            "insertChild",
            "replaceNode",
            "removeNode",
            "moveNode",
            "updateAttribute"
        };

        private static readonly string[] OperationFieldNames =
        {
            "target",
            "destination",
            "node",
            "name",
            "newName",
            "attribute",
            "value"
        };

        private const int MaximumProtocolJsonNesting = 64;

        [TestCase("addVariable", PatchOperationType.AddVariable)]
        [TestCase("renameVariable", PatchOperationType.RenameVariable)]
        [TestCase("removeVariable", PatchOperationType.RemoveVariable)]
        [TestCase("insertBefore", PatchOperationType.InsertBefore)]
        [TestCase("insertAfter", PatchOperationType.InsertAfter)]
        [TestCase("insertChild", PatchOperationType.InsertChild)]
        [TestCase("replaceNode", PatchOperationType.ReplaceNode)]
        [TestCase("removeNode", PatchOperationType.RemoveNode)]
        [TestCase("moveNode", PatchOperationType.MoveNode)]
        [TestCase("updateAttribute", PatchOperationType.UpdateAttribute)]
        public void Json_round_trip_preserves_each_operation_type(string jsonType, PatchOperationType expectedType)
        {
            var document = Patch(ValidOperation(jsonType));
            var serialized = JObject.Parse(JsonConvert.SerializeObject(document));
            var roundTripped = PatchDocument.Deserialize(serialized.ToString(Formatting.None));

            Assert.That(document.Operations[0].Type, Is.EqualTo(expectedType));
            Assert.That((string)serialized["operations"]![0]!["type"]!, Is.EqualTo(jsonType));
            Assert.That(roundTripped.Operations[0].Type, Is.EqualTo(expectedType));
        }

        [Test]
        public void Deserialize_preserves_iso_8601_shaped_json_strings()
        {
            var timestamp = "2026-07-21T12:34:56.789Z";
            var json = "{\"baseHash\":\"hash\",\"summary\":\"" + timestamp + "\",\"operations\":[{\"type\":\"updateAttribute\",\"target\":{\"id\":0},\"attribute\":\"text\",\"value\":\"" + timestamp + "\"},{\"type\":\"insertChild\",\"target\":{\"path\":\"/Program[0]/Instructions[0]\"},\"node\":{\"element\":\"Log\",\"attributes\":{\"text\":\"" + timestamp + "\"},\"children\":[]}}]}";

            var document = PatchDocument.Deserialize(json);

            Assert.That(document.Summary, Is.EqualTo(timestamp));
            Assert.That(document.Operations[0].Value, Is.EqualTo(timestamp));
            Assert.That(document.Operations[1].Node!.Attributes["text"], Is.EqualTo(timestamp));
        }

        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}],\"unexpected\":true}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0},\"unexpected\":true}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0,\"unexpected\":true}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"insertChild\",\"target\":{\"path\":\"/Program[0]/Instructions[0]\"},\"node\":{\"element\":\"Log\",\"attributes\":{},\"children\":[],\"unexpected\":true}}]}")]
        public void Deserialize_rejects_unknown_json_members(string json)
        {
            AssertDeserializeRejected(json);
        }

        [TestCase("{\"baseHash\":\"hash\",\"baseHash\":\"other\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0,\"id\":1}}]}")]
        public void Deserialize_rejects_duplicate_members_at_every_patch_protocol_level(string json)
        {
            AssertDeserializeRejected(json);
        }

        [TestCase("[]")]
        [TestCase("null")]
        [TestCase("0")]
        public void Deserialize_rejects_non_object_top_level_json(string json)
        {
            AssertDeserializeRejected(json);
        }

        [Test]
        public void Deserialize_rejects_trailing_content_after_a_valid_patch_object()
        {
            AssertDeserializeRejected(PatchJson(Op("removeNode", "target", Id(0))) + " null");
        }

        [Test]
        public void Deserialize_rejects_unknown_array_value_beyond_the_protocol_depth_limit()
        {
            var json = "{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}],\"reviewPayload\":" + NestedArray(MaximumProtocolJsonNesting + 1) + "}";

            var exception = Assert.Throws<PatchApplyException>(() => PatchDocument.Deserialize(json));

            Assert.That(exception!.Message, Does.Contain("JSON nesting depth exceeds protocol maximum of 64"));
        }

        [TestCase("{/* comment */\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",// comment\n\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        public void Deserialize_rejects_json_comments(string json)
        {
            AssertDeserializeRejected(json);
        }

        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}],}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}},]}")]
        public void Deserialize_rejects_trailing_commas(string json)
        {
            AssertDeserializeRejected(json);
        }

        [TestCase("{\"baseHash\":'hash',\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{'baseHash':\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{baseHash:\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"ha\\'sh\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"line\rbreak\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"line\nbreak\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\u00A0\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        public void Deserialize_rejects_non_rfc_json_string_and_whitespace_syntax(string json)
        {
            AssertDeserializeRejected(json);
        }

        [TestCase("+0")]
        [TestCase("00")]
        [TestCase("0.")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("0x0")]
        [TestCase("undefined")]
        public void Deserialize_rejects_non_rfc_json_number_literals(string numberLiteral)
        {
            AssertDeserializeRejected("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":" + numberLiteral + "}}]}");
        }

        [Test]
        public void Deserialize_accepts_comment_markers_inside_double_quoted_strings()
        {
            const string text = "// ordinary text /* still ordinary text */";
            var json = "{\"baseHash\":\"hash\",\"summary\":\"" + text + "\",\"operations\":[{\"type\":\"updateAttribute\",\"target\":{\"id\":0},\"attribute\":\"text\",\"value\":\"" + text + "\"}]}";

            var document = PatchDocument.Deserialize(json);

            Assert.That(document.Summary, Is.EqualTo(text));
            Assert.That(document.Operations[0].Value, Is.EqualTo(text));
        }

        [Test]
        public void Deserialize_accepts_json_delimiters_inside_double_quoted_strings()
        {
            const string text = "comma, braces {}, brackets []";
            var json = "{\"baseHash\":\"hash\",\"summary\":\"" + text + "\",\"operations\":[{\"type\":\"updateAttribute\",\"target\":{\"id\":0},\"attribute\":\"text\",\"value\":\"" + text + "\"}]}";

            var document = PatchDocument.Deserialize(json);

            Assert.That(document.Summary, Is.EqualTo(text));
            Assert.That(document.Operations[0].Value, Is.EqualTo(text));
        }

        [Test]
        public void Deserialize_accepts_all_legal_json_string_escapes()
        {
            const string expected = "\b\f\n\r\t\"/\\A";
            const string json = "{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"updateAttribute\",\"target\":{\"id\":0},\"attribute\":\"text\",\"value\":\"\\b\\f\\n\\r\\t\\\"\\/\\\\\\u0041\"}]}";

            var document = PatchDocument.Deserialize(json);

            Assert.That(document.Operations[0].Value, Is.EqualTo(expected));
        }

        [TestCase("0", 0)]
        [TestCase("-1", -1)]
        [TestCase("42", 42)]
        [TestCase("1.0", 1)]
        [TestCase("1e0", 1)]
        public void Deserialize_accepts_legal_integral_json_number_forms(string numberLiteral, int expectedId)
        {
            var document = PatchDocument.Deserialize("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":" + numberLiteral + "}}]}");

            Assert.That(document.Operations[0].Target!.Id, Is.EqualTo(expectedId));
        }

        [TestCase("2147483647", int.MaxValue)]
        [TestCase("-2147483648", int.MinValue)]
        [TestCase("-0", 0)]
        [TestCase("-0.000000000000000000000000000000000000000", 0)]
        [TestCase("1e+0", 1)]
        [TestCase("10e-1", 1)]
        [TestCase("42.000000000000000000000000000000000000000", 42)]
        [TestCase("2.147483647e9", int.MaxValue)]
        [TestCase("-2.147483648e9", int.MinValue)]
        public void Deserialize_accepts_exact_int32_selector_number_normalizations(string numberLiteral, int expectedId)
        {
            var document = PatchDocument.Deserialize("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":" + numberLiteral + "}}]}");

            Assert.That(document.Operations[0].Target!.Id, Is.EqualTo(expectedId));
        }

        [TestCase("2147483648")]
        [TestCase("-2147483649")]
        [TestCase("2147483647.1")]
        [TestCase("-2147483648.1")]
        [TestCase("1e-1")]
        [TestCase("2147483647e-1")]
        [TestCase("2.147483648e9")]
        [TestCase("-2.147483649e9")]
        [TestCase("1e999999999999999")]
        [TestCase("1e-999999999999999")]
        public void Deserialize_rejects_non_int32_or_nonintegral_selector_number_normalizations(string numberLiteral)
        {
            AssertDeserializeRejected("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":" + numberLiteral + "}}]}");
        }

        [TestCase(null)]
        [TestCase("null")]
        public void Deserialize_rejects_null_json_or_result(string? json)
        {
            AssertDeserializeRejected(json!);
        }

        [TestCase("{}")]
        [TestCase("{\"baseHash\":null,\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":null,\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"  \",\"operations\":[{\"type\":\"removeNode\",\"target\":{\"id\":0}}]}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\"}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":null}")]
        [TestCase("{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[]}")]
        public void Deserialize_rejects_invalid_patch_document_contract(string json)
        {
            AssertDeserializeRejected(json);
        }

        [Test]
        public void Deserialize_rejects_unknown_operation_type()
        {
            AssertDeserializeRejected(PatchJson(Op("notAnOperation")));
        }

        [TestCase("{}")]
        [TestCase("{\"type\":null}")]
        [TestCase("{\"type\":\"\"}")]
        public void Deserialize_rejects_missing_null_or_empty_operation_type(string operationJson)
        {
            AssertDeserializeRejected(PatchJson(operationJson));
        }

        [TestCaseSource(nameof(MissingRequiredFieldCases))]
        public void Deserialize_rejects_each_missing_required_operation_field(string operationType, string missingField, string operationJson)
        {
            Assert.Multiple(() =>
            {
                Assert.That(operationType, Is.Not.Empty);
                Assert.That(missingField, Is.Not.Empty);
                AssertDeserializeRejected(PatchJson(operationJson));
            });
        }

        [TestCaseSource(nameof(NullRequiredFieldCases))]
        public void Deserialize_rejects_each_null_required_operation_field(string operationType, string nullField, string operationJson)
        {
            Assert.Multiple(() =>
            {
                Assert.That(operationType, Is.Not.Empty);
                Assert.That(nullField, Is.Not.Empty);
                AssertDeserializeRejected(PatchJson(operationJson));
            });
        }

        [TestCaseSource(nameof(UnexpectedOperationFieldCases))]
        public void Deserialize_rejects_every_field_not_permitted_by_an_operation(string operationType, string unexpectedField, string operationJson)
        {
            Assert.Multiple(() =>
            {
                Assert.That(operationType, Is.Not.Empty);
                Assert.That(unexpectedField, Is.Not.Empty);
                AssertDeserializeRejected(PatchJson(operationJson));
            });
        }

        [TestCase("")]
        [TestCase("relative[0]")]
        [TestCase("/Program[0]/Instructions[0]/Event[x]")]
        [TestCase("//Program[0]/Instructions[0]/Event[0]")]
        [TestCase("/Program[0]/Instructions[0]/Event[-1]")]
        [TestCase("/Program[0]/Instructions[0]/Event[00]")]
        [TestCase("/Program[0]/Instructions[0]/Event[0]/")]
        [TestCase("/Program[0]/Instructions/Event[0]")]
        public void Deserialize_rejects_noncanonical_path_selectors(string path)
        {
            AssertDeserializeRejected(PatchJson(Op("removeNode", "target", Path(path))));
        }

        [TestCase("{}")]
        [TestCase("{\"id\":null}")]
        [TestCase("{\"path\":null}")]
        [TestCase("{\"id\":0,\"path\":\"/Program[0]\"}")]
        public void Deserialize_rejects_selectors_without_exactly_one_id_or_path(string selectorJson)
        {
            AssertDeserializeRejected(PatchJson("{\"type\":\"removeNode\",\"target\":" + selectorJson + "}"));
        }

        [Test]
        public void Node_selector_requires_exactly_one_id_or_path_at_construction()
        {
            Assert.That(() => new NodeSelector(null, null), Throws.TypeOf<PatchApplyException>());
            Assert.That(() => new NodeSelector(1, "/Program[0]"), Throws.TypeOf<PatchApplyException>());
            Assert.That(() => new NodeSelector(null, string.Empty), Throws.TypeOf<PatchApplyException>());
            Assert.That(() => new NodeSelector(null, "Program[0]"), Throws.TypeOf<PatchApplyException>());
        }

        [TestCase("{\"attributes\":{},\"children\":[]}")]
        [TestCase("{\"element\":null,\"attributes\":{},\"children\":[]}")]
        [TestCase("{\"element\":\"Log\",\"children\":[]}")]
        [TestCase("{\"element\":\"Log\",\"attributes\":null,\"children\":[]}")]
        [TestCase("{\"element\":\"Log\",\"attributes\":{}}")]
        [TestCase("{\"element\":\"Log\",\"attributes\":{},\"children\":null}")]
        public void Deserialize_rejects_missing_or_null_node_spec_contract_values(string nodeJson)
        {
            var operation = "{\"type\":\"insertChild\",\"target\":{\"path\":\"/Program[0]/Instructions[0]\"},\"node\":" + nodeJson + "}";

            AssertDeserializeRejected(PatchJson(operation));
        }

        [TestCase("")]
        [TestCase("1Constant")]
        [TestCase("bad name")]
        [TestCase("vizzy:Constant")]
        [TestCase("{urn:vizzy}Constant")]
        public void Node_spec_rejects_invalid_or_namespace_qualified_element_names(string element)
        {
            AssertNodeSpecRejected(() => new NodeSpec(element, EmptyAttributes(), Array.Empty<NodeSpec>()));
        }

        [TestCase("")]
        [TestCase("1text")]
        [TestCase("bad name")]
        [TestCase("vizzy:text")]
        [TestCase("{urn:vizzy}text")]
        [TestCase("xmlns")]
        public void Node_spec_rejects_invalid_or_namespace_qualified_attribute_names(string attribute)
        {
            AssertNodeSpecRejected(() => new NodeSpec("Constant", Attributes(attribute, "1"), Array.Empty<NodeSpec>()));
        }

        [TestCase("not-an-int")]
        [TestCase("1.5")]
        [TestCase("2147483648")]
        public void Node_spec_rejects_non_integer_id_values(string id)
        {
            AssertNodeSpecRejected(() => new NodeSpec("Constant", Attributes("id", id), Array.Empty<NodeSpec>()));
        }

        [Test]
        public void Node_spec_rejects_null_required_contract_values()
        {
            AssertNodeSpecRejected(() => new NodeSpec(null!, EmptyAttributes(), Array.Empty<NodeSpec>()));
            AssertNodeSpecRejected(() => new NodeSpec("Constant", null!, Array.Empty<NodeSpec>()));
            AssertNodeSpecRejected(() => new NodeSpec("Constant", EmptyAttributes(), null!));
        }

        [Test]
        public void Node_spec_rejects_duplicate_attributes_from_json()
        {
            var patch = "{\"baseHash\":\"hash\",\"summary\":\"Summary\",\"operations\":[{\"type\":\"insertChild\",\"target\":{\"path\":\"/Program[0]/Instructions[0]\"},\"node\":{\"element\":\"Constant\",\"attributes\":{\"text\":\"1\",\"text\":\"2\"},\"children\":[]}}]}";

            AssertDeserializeRejected(patch);
        }

        [Test]
        public void Node_spec_uses_ordinal_attribute_keys_and_ordered_recursive_children()
        {
            var node = new NodeSpec(
                "BinaryOp",
                Attributes("Text", "upper", "text", "lower", "op", "max"),
                new[]
                {
                    new NodeSpec("Constant", Attributes("number", "1"), Array.Empty<NodeSpec>()),
                    new NodeSpec("Constant", Attributes("number", "2"), Array.Empty<NodeSpec>())
                });

            Assert.That(node.Attributes, Has.Count.EqualTo(3));
            Assert.That(node.Attributes["Text"], Is.EqualTo("upper"));
            Assert.That(node.Attributes["text"], Is.EqualTo("lower"));
            Assert.That(node.Children.Select(child => child.Attributes["number"]), Is.EqualTo(new[] { "1", "2" }));
        }

        [Test]
        public void Node_spec_to_xelement_preserves_exact_recursive_child_order()
        {
            var node = new NodeSpec(
                "BinaryOp",
                Attributes("id", "5", "op", "max"),
                new[]
                {
                    new NodeSpec("Constant", Attributes("number", "1"), Array.Empty<NodeSpec>()),
                    new NodeSpec("Variable", Attributes("variableName", "pitch"), Array.Empty<NodeSpec>())
                });

            Assert.That(
                node.ToXElement().ToString(System.Xml.Linq.SaveOptions.DisableFormatting),
                Is.EqualTo("<BinaryOp id=\"5\" op=\"max\"><Constant number=\"1\" /><Variable variableName=\"pitch\" /></BinaryOp>"));
        }

        [Test]
        public void Node_spec_makes_defensive_read_only_copies()
        {
            var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = "1" };
            var children = new List<NodeSpec> { Node("Constant", new Dictionary<string, string> { ["text"] = "2" }) };
            var node = new NodeSpec("BinaryOp", attributes, children);

            attributes["text"] = "changed";
            children.Clear();

            Assert.That(node.Attributes["text"], Is.EqualTo("1"));
            Assert.That(node.Children, Has.Count.EqualTo(1));
            Assert.That(() => ((IDictionary<string, string>)node.Attributes).Add("new", "value"), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => ((IList<NodeSpec>)node.Children).Clear(), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Patch_document_operations_are_exposed_as_a_read_only_collection()
        {
            var document = Patch(ValidOperation("removeNode"));
            var mutableView = document.Operations as IList<PatchOperation>;

            Assert.That(document.Operations, Is.AssignableTo<IReadOnlyList<PatchOperation>>());
            Assert.That(mutableView, Is.Not.Null);
            Assert.That(() => mutableView!.Clear(), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Patch_result_makes_a_defensive_read_only_changes_copy()
        {
            var changes = new List<string> { "original change" };
            var result = new PatchResult(VizzyProgramDocument.Parse("<Program />"), changes);

            changes[0] = "changed change";

            Assert.That(result.Changes, Is.EqualTo(new[] { "original change" }));
            Assert.That(() => ((IList<string>)result.Changes).Clear(), Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void Add_variable_uses_zero_as_the_default_value_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("addVariable", "name", "throttle"),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /><Variable name=\"throttle\" number=\"0\" /></Variables><Instructions><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Add_variable_uses_the_explicit_value_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("addVariable", "name", "throttle", "value", "0.75"),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /><Variable name=\"throttle\" number=\"0.75\" /></Variables><Instructions><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Rename_variable_updates_its_direct_declaration_and_all_references_without_mutating_input()
        {
            ApplyAndAssert(
                Document("<Program><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event id=\"0\"><SetVariable id=\"1\"><Variable variableName=\"pitch\" /></SetVariable></Event></Instructions><Expressions /></Program>"),
                Op("renameVariable", "name", "pitch", "newName", "yaw"),
                "<Program><Variables><Variable name=\"yaw\" number=\"0\" /></Variables><Instructions><Event id=\"0\"><SetVariable id=\"1\"><Variable variableName=\"yaw\" /></SetVariable></Event></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Remove_variable_removes_only_its_direct_declaration_without_mutating_input()
        {
            ApplyAndAssert(
                Document("<Program><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event id=\"0\"><SetVariable id=\"1\"><Variable variableName=\"pitch\" /></SetVariable></Event></Instructions><Expressions /></Program>"),
                Op("removeVariable", "name", "pitch"),
                "<Program><Variables /><Instructions><Event id=\"0\"><SetVariable id=\"1\"><Variable variableName=\"pitch\" /></SetVariable></Event></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Insert_before_places_the_node_before_an_instruction_sibling_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("insertBefore", "target", Id(0), "node", Spec("Log", "id", "1", "text", "ready")),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Log id=\"1\" text=\"ready\" /><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Insert_after_places_the_node_after_an_instruction_sibling_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("insertAfter", "target", Id(0), "node", Spec("Log", "id", "1", "text", "ready")),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /><Log id=\"1\" text=\"ready\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Insert_child_appends_to_an_instructions_container_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("insertChild", "target", Path("/Program[0]/Instructions[0]"), "node", Spec("Wait", "id", "1", "number", "1")),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /><Wait id=\"1\" number=\"1\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Replace_node_preserves_target_pos_when_replacement_omits_pos_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("replaceNode", "target", Id(0), "node", Spec("Event", "id", "1", "event", "ReceiveMessage", "style", "message")),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"ReceiveMessage\" id=\"1\" pos=\"0,0\" style=\"message\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Replace_node_honors_explicit_pos_override_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("replaceNode", "target", Id(0), "node", Spec("Event", "id", "1", "event", "ReceiveMessage", "style", "message", "pos", "10,20")),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"ReceiveMessage\" id=\"1\" pos=\"10,20\" style=\"message\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Replace_node_can_replace_an_expression_subtree_without_mutating_input()
        {
            ApplyAndAssert(
                Document("<Program><Variables /><Instructions /><Expressions><Constant id=\"3\" number=\"1\" /></Expressions></Program>"),
                Op("replaceNode", "target", Id(3), "node", Spec("Variable", "id", "4", "variableName", "pitch")),
                "<Program><Variables /><Instructions /><Expressions><Variable id=\"4\" variableName=\"pitch\" /></Expressions></Program>");
        }

        [Test]
        public void Remove_node_removes_an_expression_or_instruction_subtree_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("removeNode", "target", Id(0)),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions /><Expressions /></Program>");
        }

        [Test]
        public void Remove_node_can_remove_an_expression_subtree_without_mutating_input()
        {
            ApplyAndAssert(
                Document("<Program><Variables /><Instructions /><Expressions><Constant id=\"3\" number=\"1\" /></Expressions></Program>"),
                Op("removeNode", "target", Id(3)),
                "<Program><Variables /><Instructions /><Expressions /></Program>");
        }

        [Test]
        public void Move_node_appends_to_the_destination_instructions_container_without_mutating_input()
        {
            ApplyAndAssert(
                Document("<Program><Variables /><Instructions><Event id=\"0\" /><While id=\"1\"><Instructions><Log id=\"2\" /></Instructions></While></Instructions><Expressions /></Program>"),
                Op("moveNode", "target", Id(0), "destination", Path("/Program[0]/Instructions[0]/While[0]/Instructions[0]")),
                "<Program><Variables /><Instructions><While id=\"1\"><Instructions><Log id=\"2\" /><Event id=\"0\" /></Instructions></While></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Update_attribute_changes_an_allowed_attribute_without_mutating_input()
        {
            ApplyAndAssert(
                Minimal(),
                Op("updateAttribute", "target", Id(0), "attribute", "event", "value", "ReceiveMessage"),
                "<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"ReceiveMessage\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>");
        }

        [Test]
        public void Apply_rejects_a_stale_base_hash_before_mutating_input()
        {
            var document = Minimal();
            var patch = PatchWithBaseHash("stale", Op("removeNode", "target", Id(0)));

            AssertApplyRejectedWithoutMutation(document, patch);
        }

        [Test]
        public void Apply_compares_base_hash_with_ordinal_semantics()
        {
            var document = Minimal();
            var differentlyCasedHash = VizzyProgramHash.Compute(document).ToUpperInvariant();

            AssertApplyRejectedWithoutMutation(
                document,
                PatchWithBaseHash(differentlyCasedHash, Op("removeNode", "target", Id(0))));
        }

        [TestCase(999)]
        [TestCase(-1)]
        public void Apply_rejects_a_missing_id_target_and_leaves_input_unchanged(int id)
        {
            var document = Minimal();

            AssertApplyRejectedWithoutMutation(document, Op("removeNode", "target", Id(id)));
        }

        [Test]
        public void Apply_rejects_a_missing_path_target_and_leaves_input_unchanged()
        {
            var document = Minimal();

            AssertApplyRejectedWithoutMutation(
                document,
                Op("removeNode", "target", Path("/Program[0]/Instructions[0]/Event[1]")));
        }

        [Test]
        public void Apply_rejects_duplicate_matching_ids_instead_of_selecting_the_first()
        {
            var document = Document("<Program><Variables /><Instructions><Event id=\"0\" /><Event id=\"0\" /></Instructions><Expressions /></Program>");

            AssertApplyRejectedWithoutMutation(document, Op("removeNode", "target", Id(0)));
        }

        [Test]
        public void Apply_rejects_duplicate_resulting_ids_and_leaves_input_unchanged()
        {
            var document = Minimal();

            AssertApplyRejectedWithoutMutation(
                document,
                Op("insertAfter", "target", Id(0), "node", Spec("Log", "id", "0")));
        }

        [Test]
        public void Apply_checks_duplicate_ids_after_all_operations_not_between_operations()
        {
            var document = Minimal();
            var originalHash = VizzyProgramHash.Compute(document);
            var patch = PatchFor(
                document,
                Op("insertAfter", "target", Id(0), "node", Spec("Log", "id", "0")),
                Op("removeNode", "target", Path("/Program[0]/Instructions[0]/Log[0]")));

            var result = VizzyPatchEngine.Apply(document, patch);

            Assert.That(result.Document.ToXml(), Is.EqualTo(document.ToXml()));
            Assert.That(VizzyProgramHash.Compute(document), Is.EqualTo(originalHash));
            Assert.That(result.Changes, Has.Count.EqualTo(2));
        }

        [Test]
        public void Apply_rejects_a_move_into_the_moved_node_itself()
        {
            var document = Document("<Program><Variables /><Instructions><Event id=\"0\"><Instructions id=\"1\"><Log id=\"2\" /></Instructions></Event></Instructions><Expressions /></Program>");

            AssertApplyRejectedWithoutMutation(
                document,
                Op("moveNode", "target", Id(1), "destination", Id(1)));
        }

        [Test]
        public void Apply_rejects_a_move_into_the_moved_nodes_descendant()
        {
            var document = Document("<Program><Variables /><Instructions><While id=\"1\"><Instructions><Log id=\"2\" /></Instructions></While></Instructions><Expressions /></Program>");

            AssertApplyRejectedWithoutMutation(
                document,
                Op("moveNode", "target", Id(1), "destination", Path("/Program[0]/Instructions[0]/While[0]/Instructions[0]")));
        }

        [TestCase("insertBefore", 1)]
        [TestCase("insertAfter", 1)]
        [TestCase("insertChild", 0)]
        [TestCase("moveNode", 0)]
        public void Apply_rejects_wrong_insert_or_move_containers(string operationType, int selectorId)
        {
            var document = Document("<Program><Variables /><Instructions><Event id=\"0\"><Log id=\"1\" /></Event></Instructions><Expressions /></Program>");
            var operation = operationType == "moveNode"
                ? Op("moveNode", "target", Id(1), "destination", Id(selectorId))
                : Op(operationType, "target", Id(selectorId), "node", Spec("Log", "id", "2"));

            AssertApplyRejectedWithoutMutation(document, operation);
        }

        [TestCaseSource(nameof(ProtectedStructuralRootCases))]
        public void Apply_rejects_protected_structural_roots(string operationJson)
        {
            AssertApplyRejectedWithoutMutation(Minimal(), JObject.Parse(operationJson));
        }

        [TestCase("removeNode")]
        [TestCase("replaceNode")]
        public void Apply_rejects_node_operations_outside_instruction_or_expression_subtrees(string operationType)
        {
            var operation = operationType == "removeNode"
                ? Op(operationType, "target", Path("/Program[0]/Variables[0]/Variable[0]"))
                : Op(operationType, "target", Path("/Program[0]/Variables[0]/Variable[0]"), "node", Spec("Variable", "name", "yaw", "number", "0"));

            AssertApplyRejectedWithoutMutation(Minimal(), operation);
        }

        [TestCase("addVariable", "pitch", null)]
        [TestCase("renameVariable", "missing", "yaw")]
        [TestCase("removeVariable", "missing", null)]
        [TestCase("renameVariable", "pitch", "yaw")]
        public void Apply_rejects_duplicate_missing_or_ambiguous_variable_names(string operationType, string name, string? newName)
        {
            var document = operationType == "renameVariable" && newName == "yaw" && name == "pitch"
                ? Document("<Program><Variables><Variable name=\"pitch\" number=\"0\" /><Variable name=\"yaw\" number=\"0\" /></Variables><Instructions /><Expressions /></Program>")
                : Minimal();
            var operation = operationType == "renameVariable"
                ? Op(operationType, "name", name, "newName", newName!)
                : Op(operationType, "name", name);

            AssertApplyRejectedWithoutMutation(document, operation);
        }

        [TestCase("renameVariable")]
        [TestCase("removeVariable")]
        public void Apply_rejects_ambiguous_duplicate_variable_declarations(string operationType)
        {
            var document = Document("<Program><Variables><Variable name=\"pitch\" number=\"0\" /><Variable name=\"pitch\" number=\"1\" /></Variables><Instructions /><Expressions /></Program>");
            var operation = operationType == "renameVariable"
                ? Op(operationType, "name", "pitch", "newName", "yaw")
                : Op(operationType, "name", "pitch");

            AssertApplyRejectedWithoutMutation(document, operation);
        }

        [TestCase("text", "ready", "<Program><Variables /><Instructions><Event id=\"0\" text=\"ready\" /></Instructions><Expressions /></Program>")]
        [TestCase("number", "2", "<Program><Variables /><Instructions><Event id=\"0\" number=\"2\" /></Instructions><Expressions /></Program>")]
        [TestCase("bool", "true", "<Program><Variables /><Instructions><Event bool=\"true\" id=\"0\" /></Instructions><Expressions /></Program>")]
        [TestCase("style", "custom-style", "<Program><Variables /><Instructions><Event id=\"0\" style=\"custom-style\" /></Instructions><Expressions /></Program>")]
        [TestCase("event", "ReceiveMessage", "<Program><Variables /><Instructions><Event event=\"ReceiveMessage\" id=\"0\" /></Instructions><Expressions /></Program>")]
        [TestCase("property", "Nav.AngleOfAttack", "<Program><Variables /><Instructions><Event id=\"0\" property=\"Nav.AngleOfAttack\" /></Instructions><Expressions /></Program>")]
        [TestCase("op", "max", "<Program><Variables /><Instructions><Event id=\"0\" op=\"max\" /></Instructions><Expressions /></Program>")]
        [TestCase("variableName", "pitch", "<Program><Variables /><Instructions><Event id=\"0\" variableName=\"pitch\" /></Instructions><Expressions /></Program>")]
        [TestCase("pos", "10,20", "<Program><Variables /><Instructions><Event id=\"0\" pos=\"10,20\" /></Instructions><Expressions /></Program>")]
        public void Update_attribute_allows_each_listed_attribute_name(string attribute, string value, string expectedXml)
        {
            ApplyAndAssert(
                Document("<Program><Variables /><Instructions><Event id=\"0\" /></Instructions><Expressions /></Program>"),
                Op("updateAttribute", "target", Id(0), "attribute", attribute, "value", value),
                expectedXml);
        }

        [TestCase("id")]
        [TestCase("")]
        [TestCase("local")]
        [TestCase("unknown")]
        [TestCase("Text")]
        public void Update_attribute_rejects_id_and_every_unlisted_attribute_name(string attribute)
        {
            AssertApplyRejectedWithoutMutation(
                Minimal(),
                Op("updateAttribute", "target", Id(0), "attribute", attribute, "value", "value"));
        }

        [TestCase("{\"type\":\"updateAttribute\",\"target\":{\"id\":0},\"attribute\":\"text\",\"value\":\"\\u0001\"}")]
        [TestCase("{\"type\":\"addVariable\",\"name\":\"yaw\",\"value\":\"\\u0001\"}")]
        [TestCase("{\"type\":\"insertAfter\",\"target\":{\"id\":0},\"node\":{\"element\":\"Log\",\"attributes\":{\"text\":\"\\u0001\"},\"children\":[]}}")]
        public void Apply_rejects_xml_invalid_control_characters_from_patch_strings_without_mutating_input(string operationJson)
        {
            var document = Minimal();
            var patch = PatchDocument.Deserialize(RawPatchJson(VizzyProgramHash.Compute(document), operationJson));

            AssertApplyRejectedWithoutMutation(document, patch);
        }

        [Test]
        public void Apply_runs_operations_in_order_and_returns_one_deterministic_human_readable_line_per_operation()
        {
            var document = Minimal();
            var originalHash = VizzyProgramHash.Compute(document);
            var patch = PatchFor(
                document,
                Op("addVariable", "name", "yaw"),
                Op("renameVariable", "name", "yaw", "newName", "heading"),
                Op("updateAttribute", "target", Id(0), "attribute", "event", "value", "ReceiveMessage"));

            var result = VizzyPatchEngine.Apply(document, patch);
            var repeated = VizzyPatchEngine.Apply(document, patch);

            Assert.That(
                result.Document.ToXml(),
                Is.EqualTo("<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /><Variable name=\"heading\" number=\"0\" /></Variables><Instructions><Event event=\"ReceiveMessage\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>"));
            Assert.That(VizzyProgramHash.Compute(document), Is.EqualTo(originalHash));
            Assert.That(result.Changes, Has.Count.EqualTo(3));
            Assert.That(result.Changes.All(change => !string.IsNullOrWhiteSpace(change)), Is.True);
            Assert.That(result.Changes.All(change => !change.Contains("\r") && !change.Contains("\n")), Is.True);
            Assert.That(result.Changes, Is.EqualTo(repeated.Changes));
        }

        private static void ApplyAndAssert(VizzyProgramDocument document, JObject operation, string expectedXml)
        {
            var originalHash = VizzyProgramHash.Compute(document);

            var result = VizzyPatchEngine.Apply(document, PatchFor(document, operation));

            Assert.That(result.Document.ToXml(), Is.EqualTo(expectedXml));
            Assert.That(VizzyProgramHash.Compute(document), Is.EqualTo(originalHash));
            Assert.That(result.Changes, Has.Count.EqualTo(1));
            Assert.That(result.Changes[0], Is.Not.Null.And.Not.Empty);
            Assert.That(result.Changes[0], Does.Not.Contain("\r").And.Not.Contain("\n"));
        }

        private static void AssertApplyRejectedWithoutMutation(VizzyProgramDocument document, JObject operation)
        {
            AssertApplyRejectedWithoutMutation(document, PatchFor(document, operation));
        }

        private static void AssertApplyRejectedWithoutMutation(VizzyProgramDocument document, PatchDocument patch)
        {
            var originalHash = VizzyProgramHash.Compute(document);
            var exception = Assert.Throws<PatchApplyException>(() => VizzyPatchEngine.Apply(document, patch));

            Assert.That(exception!.Message, Is.Not.Null.And.Not.Empty);
            Assert.That(VizzyProgramHash.Compute(document), Is.EqualTo(originalHash));
        }

        private static void AssertDeserializeRejected(string json)
        {
            var exception = Assert.Throws<PatchApplyException>(() => PatchDocument.Deserialize(json));

            Assert.That(exception!.Message, Is.Not.Null.And.Not.Empty);
        }

        private static void AssertNodeSpecRejected(Func<NodeSpec> factory)
        {
            var exception = Assert.Throws<PatchApplyException>(() => factory().ToXElement());

            Assert.That(exception!.Message, Is.Not.Null.And.Not.Empty);
        }

        private static PatchDocument PatchFor(VizzyProgramDocument document, params JObject[] operations) =>
            PatchWithBaseHash(VizzyProgramHash.Compute(document), operations);

        private static PatchDocument Patch(params JObject[] operations) => PatchWithBaseHash("hash", operations);

        private static PatchDocument PatchWithBaseHash(string baseHash, params JObject[] operations) =>
            PatchDocument.Deserialize(PatchJson(baseHash, operations));

        private static string PatchJson(params JObject[] operations) => PatchJson("hash", operations);

        private static string PatchJson(string operationJson) =>
            PatchJson(JObject.Parse(operationJson));

        private static string RawPatchJson(string baseHash, string operationJson) =>
            "{\"baseHash\":\"" + baseHash + "\",\"summary\":\"Test patch\",\"operations\":[" + operationJson + "]}";

        private static string PatchJson(string baseHash, params JObject[] operations)
        {
            return new JObject
            {
                ["baseHash"] = baseHash,
                ["summary"] = "Test patch",
                ["operations"] = new JArray(operations.Select(operation => operation.DeepClone()))
            }.ToString(Formatting.None);
        }

        private static JObject Op(string type, params object[] fields)
        {
            if (fields.Length % 2 != 0)
            {
                throw new ArgumentException("Operation fields must be name/value pairs.", nameof(fields));
            }

            var operation = new JObject { ["type"] = type };
            for (var index = 0; index < fields.Length; index += 2)
            {
                operation[(string)fields[index]] = ToToken(fields[index + 1]);
            }

            return operation;
        }

        private static JObject Id(int id) => new JObject { ["id"] = id };

        private static string NestedArray(int depth) =>
            new string('[', depth) + "0" + new string(']', depth);

        private static JObject Path(string path) => new JObject { ["path"] = path };

        private static JObject Spec(string element, params string[] attributes) =>
            new JObject
            {
                ["element"] = element,
                ["attributes"] = JObject.FromObject(Attributes(attributes)),
                ["children"] = new JArray()
            };

        private static JToken ToToken(object? value)
        {
            if (value == null)
            {
                return JValue.CreateNull();
            }

            return value is JToken token ? token.DeepClone() : JToken.FromObject(value);
        }

        private static Dictionary<string, string> EmptyAttributes() =>
            new Dictionary<string, string>(StringComparer.Ordinal);

        private static Dictionary<string, string> Attributes(params string[] nameValuePairs)
        {
            if (nameValuePairs.Length % 2 != 0)
            {
                throw new ArgumentException("Attributes must be name/value pairs.", nameof(nameValuePairs));
            }

            var attributes = EmptyAttributes();
            for (var index = 0; index < nameValuePairs.Length; index += 2)
            {
                attributes.Add(nameValuePairs[index], nameValuePairs[index + 1]);
            }

            return attributes;
        }

        private static VizzyProgramDocument Minimal() =>
            Document("<Program name=\"Minimal\"><Variables><Variable name=\"pitch\" number=\"0\" /></Variables><Instructions><Event event=\"FlightStart\" id=\"0\" pos=\"0,0\" style=\"flight-start\" /></Instructions><Expressions /></Program>");

        private static VizzyProgramDocument Document(string xml) => VizzyProgramDocument.Parse(xml);

        private static NodeSpec Node(string element, IReadOnlyDictionary<string, string>? attributes) =>
            new NodeSpec(element, attributes ?? EmptyAttributes(), Array.Empty<NodeSpec>());

        public static IEnumerable<TestCaseData> MissingRequiredFieldCases()
        {
            foreach (var operationType in OperationTypes)
            {
                foreach (var field in RequiredFieldsFor(operationType))
                {
                    var operation = ValidOperation(operationType);
                    operation.Remove(field);
                    yield return new TestCaseData(operationType, field, operation.ToString(Formatting.None))
                        .SetName("Deserialize_rejects_" + operationType + "_without_" + field);
                }
            }
        }

        public static IEnumerable<TestCaseData> NullRequiredFieldCases()
        {
            foreach (var operationType in OperationTypes)
            {
                foreach (var field in RequiredFieldsFor(operationType))
                {
                    var operation = ValidOperation(operationType);
                    operation[field] = JValue.CreateNull();
                    yield return new TestCaseData(operationType, field, operation.ToString(Formatting.None))
                        .SetName("Deserialize_rejects_" + operationType + "_null_" + field);
                }
            }
        }

        public static IEnumerable<TestCaseData> UnexpectedOperationFieldCases()
        {
            foreach (var operationType in OperationTypes)
            {
                var allowed = new HashSet<string>(AllowedFieldsFor(operationType), StringComparer.Ordinal);
                foreach (var field in OperationFieldNames.Where(field => !allowed.Contains(field)))
                {
                    var operation = ValidOperation(operationType);
                    operation[field] = SampleValueFor(field);
                    yield return new TestCaseData(operationType, field, operation.ToString(Formatting.None))
                        .SetName("Deserialize_rejects_" + operationType + "_field_" + field);
                }
            }
        }

        public static IEnumerable<TestCaseData> ProtectedStructuralRootCases()
        {
            var paths = new[]
            {
                "/Program[0]",
                "/Program[0]/Variables[0]",
                "/Program[0]/Instructions[0]",
                "/Program[0]/Expressions[0]"
            };

            foreach (var operationType in new[] { "removeNode", "replaceNode" })
            {
                foreach (var path in paths)
                {
                    var operation = operationType == "removeNode"
                        ? Op(operationType, "target", Path(path))
                        : Op(operationType, "target", Path(path), "node", Spec("Log"));
                    yield return new TestCaseData(operation.ToString(Formatting.None))
                        .SetName(operationType + "_rejects_structural_root_" + path.Replace('/', '_'));
                }
            }
        }

        private static JObject ValidOperation(string operationType)
        {
            return operationType switch
            {
                "addVariable" => Op(operationType, "name", "yaw"),
                "renameVariable" => Op(operationType, "name", "pitch", "newName", "yaw"),
                "removeVariable" => Op(operationType, "name", "pitch"),
                "insertBefore" => Op(operationType, "target", Id(0), "node", Spec("Log")),
                "insertAfter" => Op(operationType, "target", Id(0), "node", Spec("Log")),
                "insertChild" => Op(operationType, "target", Path("/Program[0]/Instructions[0]"), "node", Spec("Log")),
                "replaceNode" => Op(operationType, "target", Id(0), "node", Spec("Log")),
                "removeNode" => Op(operationType, "target", Id(0)),
                "moveNode" => Op(operationType, "target", Id(0), "destination", Path("/Program[0]/Instructions[0]")),
                "updateAttribute" => Op(operationType, "target", Id(0), "attribute", "event", "value", "ReceiveMessage"),
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, "Unknown patch operation type.")
            };
        }

        private static JToken SampleValueFor(string field)
        {
            return field switch
            {
                "target" => Id(0),
                "destination" => Path("/Program[0]/Instructions[0]"),
                "node" => Spec("Log"),
                "name" => new JValue("pitch"),
                "newName" => new JValue("yaw"),
                "attribute" => new JValue("event"),
                "value" => new JValue("ReceiveMessage"),
                _ => throw new ArgumentOutOfRangeException(nameof(field), field, "Unknown operation field.")
            };
        }

        private static string[] RequiredFieldsFor(string operationType)
        {
            return operationType switch
            {
                "addVariable" => new[] { "name" },
                "renameVariable" => new[] { "name", "newName" },
                "removeVariable" => new[] { "name" },
                "insertBefore" => new[] { "target", "node" },
                "insertAfter" => new[] { "target", "node" },
                "insertChild" => new[] { "target", "node" },
                "replaceNode" => new[] { "target", "node" },
                "removeNode" => new[] { "target" },
                "moveNode" => new[] { "target", "destination" },
                "updateAttribute" => new[] { "target", "attribute", "value" },
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, "Unknown patch operation type.")
            };
        }

        private static string[] AllowedFieldsFor(string operationType)
        {
            return operationType switch
            {
                "addVariable" => new[] { "name", "value" },
                "renameVariable" => new[] { "name", "newName" },
                "removeVariable" => new[] { "name" },
                "insertBefore" => new[] { "target", "node" },
                "insertAfter" => new[] { "target", "node" },
                "insertChild" => new[] { "target", "node" },
                "replaceNode" => new[] { "target", "node" },
                "removeNode" => new[] { "target" },
                "moveNode" => new[] { "target", "destination" },
                "updateAttribute" => new[] { "target", "attribute", "value" },
                _ => throw new ArgumentOutOfRangeException(nameof(operationType), operationType, "Unknown patch operation type.")
            };
        }
    }
}
