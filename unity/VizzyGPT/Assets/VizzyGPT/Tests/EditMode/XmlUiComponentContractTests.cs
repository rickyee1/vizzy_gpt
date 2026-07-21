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
            var document = Validate(resourcePath, schemaPath);
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

        private static IEnumerable<TestCaseData> Resources()
        {
            yield return new TestCaseData("VizzyGptPanel.xml", PanelIds);
            yield return new TestCaseData("PreviewDialog.xml", PreviewIds);
            yield return new TestCaseData("SettingsDialog.xml", SettingsIds);
        }

        private static XmlDocument Validate(string resourcePath, string schemaPath)
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

            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            return document;
        }
    }
}
