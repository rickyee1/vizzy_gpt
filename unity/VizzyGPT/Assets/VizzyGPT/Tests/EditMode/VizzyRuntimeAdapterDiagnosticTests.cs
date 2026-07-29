using System;
using System.Reflection;
using NUnit.Framework;
using VizzyGPT.Runtime.Adapters;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class VizzyRuntimeAdapterDiagnosticTests
    {
        [Test]
        public void Adapter_exposes_an_internal_runtime_validation_constructor()
        {
            var constructor = typeof(VizzyRuntimeAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Func<string, Exception>) },
                null);

            Assert.That(constructor, Is.Not.Null);
        }

        [Test]
        public void Serializer_validation_unwraps_runtime_exceptions_into_staged_diagnostics()
        {
            var adapter = CreateAdapter(_ => new TargetInvocationException(
                new InvalidOperationException("Bad style.")));

            var issue = adapter.ValidateWithProgramSerializer("<Program>");

            Assert.That(issue, Is.Not.Null);
            Assert.That(issue.Message, Does.StartWith("Program XML is not accepted by the current runtime: "));
            Assert.That(issue.Message, Does.Contain("Bad style."));
            Assert.That(issue.Message, Does.Contain("RuntimeValidation"));
            Assert.That(issue.Message, Does.Not.Contain("target of an invocation"));
        }

        private static VizzyRuntimeAdapter CreateAdapter(Func<string, Exception> runtimeValidation)
        {
            var constructor = typeof(VizzyRuntimeAdapter).GetConstructor(
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Func<string, Exception>) },
                null);

            return (VizzyRuntimeAdapter)constructor.Invoke(new object[] { runtimeValidation });
        }
    }
}
