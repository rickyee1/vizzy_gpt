#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Xml.Schema;
using NUnit.Framework;
using UnityEngine;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class XmlUiComponentContractTests
    {
        private static readonly string[] PanelIds =
        {
            "gpt-launcher-button", "vizzy-gpt-panel", "settings-button", "close-button",
            "ask-toggle", "modify-toggle", "transcript-text", "status-text", "pending-indicator",
            "prompt-input", "undo-button", "preview-button", "send-button", "cancel-button"
        };

        private static readonly string[] PreviewIds =
        {
            "preview-dialog", "summary-text", "added-text", "changed-text", "removed-text",
            "warnings-text", "apply-button", "cancel-button"
        };

        private static readonly string[] SettingsIds =
        {
            "settings-dialog", "base-url-input", "api-mode-input", "model-input", "api-key-input",
            "timeout-input", "destination-host-text", "connection-status-text", "test-button",
            "save-button", "cancel-button"
        };

        [TestCaseSource(nameof(Resources))]
        public void Ui_resource_validates_against_modtools_schema_and_contains_required_controls(
            string resourceName,
            string[] requiredIds)
        {
            var root = Application.dataPath;
            var schemaPath = Path.Combine(root, "ModTools", "UI", "XmlLayout.xsd");
            var resourcePath = Path.Combine(root, "VizzyGPT", "Runtime", "Resources", "Ui", resourceName);
            var document = Validate(resourcePath, schemaPath, resourceName == "VizzyGptPanel.xml");
            var namespaceManager = new XmlNamespaceManager(document.NameTable);
            namespaceManager.AddNamespace("ui", "http://www.w3schools.com");

            foreach (var id in requiredIds)
            {
                Assert.That(document.SelectSingleNode("//*[@id='" + id + "']", namespaceManager), Is.Not.Null, id);
            }

            if (resourceName == "VizzyGptPanel.xml")
            {
                Assert.That(document.SelectSingleNode("//ui:TextMeshProInputField[@id='prompt-input']", namespaceManager), Is.Not.Null);
            }
        }

        [Test]
        public void Mod_data_declares_the_ui_resource_database()
        {
            var modDataPath = Path.Combine(Application.dataPath, "ModData.asset");
            var modData = File.ReadAllText(modDataPath);

            Assert.That(modData, Does.Contain("_uiResourceDatabases:\n  - {fileID: 11400000, guid: 779cac5c7816ca948aa58773c0d0ee45, type: 3}"));
        }

        [TestCase("VizzyGPT/VizzyGptPanel", "f1348ea3406c6e747858a07a2574e98a")]
        [TestCase("VizzyGPT/PreviewDialog", "56c2f77f8adaa1048aa5e8d05f87653a")]
        [TestCase("VizzyGPT/SettingsDialog", "c430157d929be5a4eb22b5fc340c018b")]
        public void Ui_resource_database_declares_each_packaged_xml(string resourcePath, string guid)
        {
            var databasePath = Path.Combine(Application.dataPath, "Content", "XML UI", "UIResourceDatabase.asset");
            var database = File.ReadAllText(databasePath);

            Assert.That(database, Does.Contain("AutomaticallyRemoveEntries: 0"));
            Assert.That(database, Does.Contain(
                "- path: " + resourcePath + "\n    resource: {fileID: 4900000, guid: " + guid + ", type: 3}"));
        }

        [Test]
        public void Behaviour_uses_documented_ui_resource_database_paths()
        {
            var behaviourPath = Path.Combine(Application.dataPath, "VizzyGPT", "Runtime", "VizzyGptBehaviour.cs");
            var source = File.ReadAllText(behaviourPath);

            Assert.That(source, Does.Contain("VizzyGPT/VizzyGptPanel"));
            Assert.That(source, Does.Contain("VizzyGPT/PreviewDialog"));
            Assert.That(source, Does.Contain("VizzyGPT/SettingsDialog"));
            Assert.That(source, Does.Contain("ResourceDatabase.GetResource<TextAsset>"));
            Assert.That(source, Does.Not.Contain("Resources.Load<TextAsset>"));
        }

        [Test]
        public void Panel_buttons_use_textmeshpro_children_not_legacy_text_attributes()
        {
            var resourcePath = Path.Combine(Application.dataPath, "VizzyGPT", "Runtime", "Resources", "Ui", "VizzyGptPanel.xml");
            var document = new XmlDocument();
            document.Load(resourcePath);
            var namespaceManager = new XmlNamespaceManager(document.NameTable);
            namespaceManager.AddNamespace("ui", "http://www.w3schools.com");

            var buttons = document.SelectNodes("//ui:Button|//ui:ToggleButton", namespaceManager);
            Assert.That(buttons, Is.Not.Null);
            foreach (XmlElement button in buttons!)
            {
                Assert.That(button.HasAttribute("text"), Is.False, button.Name + " must not use the legacy text attribute.");
            }

            var toggles = document.SelectNodes("//ui:ToggleButton", namespaceManager);
            Assert.That(toggles, Is.Not.Null);
            foreach (XmlElement toggle in toggles!)
            {
                Assert.That(toggle.SelectSingleNode("ui:TextMeshPro", namespaceManager), Is.Not.Null,
                    "ToggleButton must use a TextMeshPro child.");
            }

            AssertToggle(document, namespaceManager, "ask-toggle", "Ask", "OnAskModeClicked();");
            AssertToggle(document, namespaceManager, "modify-toggle", "Modify", "OnModifyModeClicked();");
        }

        [Test]
        public void Task_dialog_surfaces_do_not_use_padding_panels_as_layout_containers()
        {
            AssertSurfaceHasStableGeometry("VizzyGptPanel.xml", "vizzy-gpt-panel", "transcript-scroll", "prompt-input");
            AssertSurfaceHasStableGeometry("PreviewDialog.xml", "preview-dialog-content", "preview-scroll", "apply-button");
            AssertSurfaceHasStableGeometry("SettingsDialog.xml", "settings-dialog-content", "settings-form", "save-button");
        }

        [Test]
        public void Key_panel_text_and_mode_controls_have_explicit_non_overlapping_geometry()
        {
            var panel = LoadResource("VizzyGptPanel.xml");
            AssertAnchoredRect(panel, "panel-title", "0 1", "0 1", "0 1", "12 -44", "240 -12");
            AssertAnchoredRect(panel, "mode-label", "0 1", "0 1", "0 1", "0 -20", "80 0");
            AssertAnchoredRect(panel, "status-text", "0 0", "0 0", "0 0", "12 98", "312 122");
            AssertAnchoredRect(panel, "pending-indicator", "0 0", "0 0", "0 0", "326 104", "336 114");
            AssertToggleHalf(panel, "ask-toggle", "LowerLeft");
            AssertToggleHalf(panel, "modify-toggle", "LowerRight");
            AssertNonOverlappingToggleHalves(panel);
            AssertPanelVerticalBands(panel);

            var preview = LoadResource("PreviewDialog.xml");
            AssertAnchoredRect(preview, "preview-title", "0 1", "0 1", "0 1", "20 -52", "400 -20");
            var settings = LoadResource("SettingsDialog.xml");
            AssertAnchoredRect(settings, "settings-title", "0 1", "0 1", "0 1", "20 -52", "400 -20");
        }

        private static IEnumerable<TestCaseData> Resources()
        {
            yield return new TestCaseData("VizzyGptPanel.xml", PanelIds);
            yield return new TestCaseData("PreviewDialog.xml", PreviewIds);
            yield return new TestCaseData("SettingsDialog.xml", SettingsIds);
        }

        private static XmlDocument Validate(string resourcePath, string schemaPath, bool allowStockToggleButtonText)
        {
            var schemas = new XmlSchemaSet();
            schemas.Add("http://www.w3schools.com", schemaPath);
            var errors = new List<XmlSchemaException>();
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };
            settings.ValidationEventHandler += (_, args) => errors.Add(args.Exception);
            var document = new XmlDocument();
            using (var reader = XmlReader.Create(resourcePath, settings))
            {
                document.Load(reader);
            }

            if (allowStockToggleButtonText)
            {
                var lines = File.ReadAllLines(resourcePath);
                var knownLines = new HashSet<int>();
                for (var index = 0; index < lines.Length; index++)
                {
                    if (lines[index].Contains("<TextMeshPro text=\"Ask\"") || lines[index].Contains("<TextMeshPro text=\"Modify\""))
                    {
                        knownLines.Add(index + 1);
                    }
                }

                var knownErrors = errors.FindAll(error => IsKnownStockToggleButtonChildError(error, knownLines));
                Assert.That(knownLines, Has.Count.EqualTo(2));
                Assert.That(knownErrors, Has.Count.EqualTo(2), "Expected only the two known stock ToggleButton schema false positives.");
                errors.RemoveAll(error => IsKnownStockToggleButtonChildError(error, knownLines));
            }

            Assert.That(errors, Is.Empty, string.Join("\n", errors.ConvertAll(error => error.Message)));
            return document;
        }

        private static void AssertSurfaceHasStableGeometry(string resourceName, string surfaceId, string contentId, string actionId)
        {
            var resourcePath = Path.Combine(Application.dataPath, "VizzyGPT", "Runtime", "Resources", "Ui", resourceName);
            var document = new XmlDocument();
            document.Load(resourcePath);
            var surface = document.SelectSingleNode("//*[@id='" + surfaceId + "']") as XmlElement;
            var content = document.SelectSingleNode("//*[@id='" + contentId + "']") as XmlElement;
            var action = document.SelectSingleNode("//*[@id='" + actionId + "']") as XmlElement;

            Assert.That(surface, Is.Not.Null, surfaceId);
            Assert.That(surface!.HasAttribute("padding"), Is.False, surfaceId + " must not become a horizontal padding layout group.");
            Assert.That(content, Is.Not.Null, contentId);
            Assert.That(content!.GetAttribute("anchorMin"), Is.EqualTo("0 0"));
            Assert.That(content.GetAttribute("anchorMax"), Is.EqualTo("1 1"));
            Assert.That(action, Is.Not.Null, actionId);
            Assert.That(action!.HasAttribute("width") || action.HasAttribute("offsetXY"), Is.True, actionId);
        }

        private static bool IsKnownStockToggleButtonChildError(XmlSchemaException error, ISet<int> knownLines)
        {
            return knownLines.Contains(error.LineNumber)
                && error.LinePosition > 0
                && error.Message.IndexOf("ToggleButton", StringComparison.Ordinal) >= 0
                && error.Message.IndexOf("TextMeshPro", StringComparison.Ordinal) >= 0;
        }

        private static void AssertToggle(XmlDocument document, XmlNamespaceManager namespaceManager, string id, string text, string onValueChanged)
        {
            var toggle = document.SelectSingleNode("//*[@id='" + id + "']", namespaceManager) as XmlElement;
            Assert.That(toggle, Is.Not.Null, id);
            Assert.That(toggle!.GetAttribute("onValueChanged"), Is.EqualTo(onValueChanged));
            var labels = toggle.SelectNodes("ui:TextMeshPro", namespaceManager);
            Assert.That(labels, Is.Not.Null);
            Assert.That(labels!.Count, Is.EqualTo(1), id + " must have exactly one label.");
            Assert.That(((XmlElement)labels[0]!).GetAttribute("text"), Is.EqualTo(text));
        }

        private static XmlDocument LoadResource(string resourceName)
        {
            var path = Path.Combine(Application.dataPath, "VizzyGPT", "Runtime", "Resources", "Ui", resourceName);
            var document = new XmlDocument();
            document.Load(path);
            return document;
        }

        private static void AssertAnchoredRect(XmlDocument document, string id, string anchorMin, string anchorMax, string pivot, string offsetMin, string offsetMax)
        {
            var element = document.SelectSingleNode("//*[@id='" + id + "']") as XmlElement;
            Assert.That(element, Is.Not.Null, id);
            Assert.That(element!.GetAttribute("anchorMin"), Is.EqualTo(anchorMin), id);
            Assert.That(element.GetAttribute("anchorMax"), Is.EqualTo(anchorMax), id);
            Assert.That(element.GetAttribute("pivot"), Is.EqualTo(pivot), id);
            Assert.That(element.GetAttribute("offsetMin"), Is.EqualTo(offsetMin), id);
            Assert.That(element.GetAttribute("offsetMax"), Is.EqualTo(offsetMax), id);

            var parsedAnchorMin = ParsePair(anchorMin);
            var parsedAnchorMax = ParsePair(anchorMax);
            var parsedOffsetMin = ParsePair(offsetMin);
            var parsedOffsetMax = ParsePair(offsetMax);
            Assert.That(parsedAnchorMin.X, Is.InRange(0, 1), id + " anchorMin.x");
            Assert.That(parsedAnchorMin.Y, Is.InRange(0, 1), id + " anchorMin.y");
            Assert.That(parsedAnchorMax.X, Is.InRange(0, 1), id + " anchorMax.x");
            Assert.That(parsedAnchorMax.Y, Is.InRange(0, 1), id + " anchorMax.y");
            Assert.That(parsedAnchorMin.X, Is.LessThanOrEqualTo(parsedAnchorMax.X), id + " anchors x");
            Assert.That(parsedAnchorMin.Y, Is.LessThanOrEqualTo(parsedAnchorMax.Y), id + " anchors y");
            Assert.That(parsedOffsetMin.X, Is.LessThan(parsedOffsetMax.X), id + " width");
            Assert.That(parsedOffsetMin.Y, Is.LessThan(parsedOffsetMax.Y), id + " height");
        }

        private static void AssertToggleHalf(XmlDocument document, string id, string alignment)
        {
            var element = document.SelectSingleNode("//*[@id='" + id + "']") as XmlElement;
            Assert.That(element, Is.Not.Null, id);
            Assert.That(element!.GetAttribute("rectAlignment"), Is.EqualTo(alignment), id);
            Assert.That(element.GetAttribute("width"), Is.EqualTo("138"), id);
            Assert.That(element.GetAttribute("height"), Is.EqualTo("25"), id);
        }

        private static void AssertNonOverlappingToggleHalves(XmlDocument document)
        {
            var group = document.SelectSingleNode("//*[@id='mode-toggle-group']") as XmlElement;
            Assert.That(group, Is.Not.Null);
            var groupWidth = ParseInt(group!.GetAttribute("width"));
            var groupHeight = ParseInt(group.GetAttribute("height"));
            var askWidth = ParseInt(((XmlElement)document.SelectSingleNode("//*[@id='ask-toggle']")!).GetAttribute("width"));
            var modifyWidth = ParseInt(((XmlElement)document.SelectSingleNode("//*[@id='modify-toggle']")!).GetAttribute("width"));
            var askHeight = ParseInt(((XmlElement)document.SelectSingleNode("//*[@id='ask-toggle']")!).GetAttribute("height"));
            var modifyHeight = ParseInt(((XmlElement)document.SelectSingleNode("//*[@id='modify-toggle']")!).GetAttribute("height"));
            var ask = new Rect(0, 0, askWidth, askHeight);
            var modify = new Rect(groupWidth - modifyWidth, 0, modifyWidth, modifyHeight);

            Assert.That(ask.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(ask.xMax, Is.LessThanOrEqualTo(groupWidth));
            Assert.That(ask.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(ask.yMax, Is.LessThanOrEqualTo(groupHeight));
            Assert.That(modify.xMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(modify.xMax, Is.LessThanOrEqualTo(groupWidth));
            Assert.That(modify.yMin, Is.GreaterThanOrEqualTo(0));
            Assert.That(modify.yMax, Is.LessThanOrEqualTo(groupHeight));
            Assert.That(ask.Overlaps(modify), Is.False, "Ask and Modify rectangles overlap.");
        }

        private static void AssertPanelVerticalBands(XmlDocument document)
        {
            var prompt = document.SelectSingleNode("//*[@id='prompt-input']") as XmlElement;
            var status = document.SelectSingleNode("//*[@id='status-text']") as XmlElement;
            var pending = document.SelectSingleNode("//*[@id='pending-indicator']") as XmlElement;
            var transcript = document.SelectSingleNode("//*[@id='transcript-scroll']") as XmlElement;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(status, Is.Not.Null);
            Assert.That(pending, Is.Not.Null);
            Assert.That(transcript, Is.Not.Null);

            var promptBottom = ParseSecondInt(prompt!.GetAttribute("offsetXY"));
            var promptTop = promptBottom + ParseInt(prompt.GetAttribute("height"));
            var statusRect = new Rect(ParseFirstInt(status!.GetAttribute("offsetMin")), ParseSecondInt(status.GetAttribute("offsetMin")),
                ParseFirstInt(status.GetAttribute("offsetMax")) - ParseFirstInt(status.GetAttribute("offsetMin")),
                ParseSecondInt(status.GetAttribute("offsetMax")) - ParseSecondInt(status.GetAttribute("offsetMin")));
            var pendingRect = new Rect(ParseFirstInt(pending!.GetAttribute("offsetMin")), ParseSecondInt(pending.GetAttribute("offsetMin")),
                ParseFirstInt(pending.GetAttribute("offsetMax")) - ParseFirstInt(pending.GetAttribute("offsetMin")),
                ParseSecondInt(pending.GetAttribute("offsetMax")) - ParseSecondInt(pending.GetAttribute("offsetMin")));
            var transcriptBottom = ParseSecondInt(transcript!.GetAttribute("offsetMin"));
            var transcriptTop = ParseSecondInt(transcript.GetAttribute("offsetMax"));

            Assert.That(statusRect.yMin, Is.GreaterThan(promptTop), "Status overlaps prompt.");
            Assert.That(pendingRect.yMin, Is.GreaterThan(promptTop), "Pending indicator overlaps prompt.");
            Assert.That(transcriptBottom, Is.GreaterThan(statusRect.yMax), "Transcript overlaps status.");
            Assert.That(transcriptBottom, Is.GreaterThan(pendingRect.yMax), "Transcript overlaps pending indicator.");
            Assert.That(transcriptBottom, Is.GreaterThan(0), "Transcript bottom offset must reserve lower controls.");
            Assert.That(transcriptTop, Is.LessThan(0), "Transcript top offset must reserve upper controls.");
            Assert.That(statusRect.width, Is.GreaterThan(0));
            Assert.That(statusRect.height, Is.GreaterThan(0));
            Assert.That(pendingRect.width, Is.GreaterThan(0));
            Assert.That(pendingRect.height, Is.GreaterThan(0));
        }

        private static int ParseInt(string value)
        {
            return int.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static int ParseSecondInt(string value)
        {
            return ParsePair(value).Y;
        }

        private static int ParseFirstInt(string value)
        {
            return ParsePair(value).X;
        }

        private static IntPair ParsePair(string value)
        {
            var values = value.Split(' ');
            Assert.That(values, Has.Length.EqualTo(2));
            return new IntPair(ParseInt(values[0]), ParseInt(values[1]));
        }

        private readonly struct IntPair
        {
            public IntPair(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X { get; }

            public int Y { get; }
        }
    }
}
