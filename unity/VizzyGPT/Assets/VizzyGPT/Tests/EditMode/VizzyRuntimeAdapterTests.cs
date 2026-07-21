using NUnit.Framework;
using VizzyGPT.Core.Validation;
using VizzyGPT.Runtime.Adapters;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyRuntimeAdapterTests
    {
        [Test]
        public void Serializer_validation_returns_an_error_for_malformed_xml()
        {
            var adapter = new VizzyRuntimeAdapter();

            var issue = adapter.ValidateWithProgramSerializer("<Program>");

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Severity, Is.EqualTo(ValidationSeverity.Error));
            Assert.That(issue.Code, Is.EqualTo("RuntimeSerializer"));
        }
    }
}
