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
        private const string StockToggleButtonSchemaError =
            "The element 'http://www.w3schools.com:ToggleButton' cannot contain child element " +
            "'http://www.w3schools.com:TextMeshPro' because the parent element's content model is text only.";
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
            var errors = new List<string>();
            var settings = new XmlReaderSettings
            {
                ValidationType = ValidationType.Schema,
                Schemas = schemas
            };
            settings.ValidationEventHandler += (_, args) => errors.Add(args.Message);
            var document = new XmlDocument();
            using (var reader = XmlReader.Create(resourcePath, settings))
            {
                document.Load(reader);
            }

            if (allowStockToggleButtonText)
            {
                var knownErrors = errors.FindAll(error => string.Equals(error, StockToggleButtonSchemaError, StringComparison.Ordinal));
                Assert.That(knownErrors, Has.Count.EqualTo(2), "Expected only the two known stock ToggleButton schema false positives.");
                errors.RemoveAll(error => string.Equals(error, StockToggleButtonSchemaError, StringComparison.Ordinal));
            }

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
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
    }
}
