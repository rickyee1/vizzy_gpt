#nullable enable

using NUnit.Framework;
using TMPro;
using UnityEngine.UI;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class XmlUiComponentContractTests
    {
        [Test]
        public void Stock_xml_control_types_are_available_to_runtime_bindings()
        {
            Assert.That(typeof(TMP_InputField).Namespace, Is.EqualTo("TMPro"));
            Assert.That(typeof(TMP_Text).Namespace, Is.EqualTo("TMPro"));
            Assert.That(typeof(Button).Namespace, Is.EqualTo("UnityEngine.UI"));
        }
    }
}
