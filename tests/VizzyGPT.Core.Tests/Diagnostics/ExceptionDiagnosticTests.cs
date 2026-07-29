using System;
using System.Reflection;
using NUnit.Framework;
using VizzyGPT.Core.Diagnostics;

namespace VizzyGPT.Core.Tests.Diagnostics
{
    public sealed class ExceptionDiagnosticTests
    {
        [Test]
        public void From_unwraps_target_invocation_and_redacts_bearer_tokens()
        {
            var wrapped = new TargetInvocationException(
                new InvalidOperationException("Bad style. Authorization: Bearer secret-token"));

            var diagnostic = ExceptionDiagnostic.From(wrapped, "RuntimeValidation");

            Assert.That(diagnostic.Code, Is.EqualTo("InvalidOperationException"));
            Assert.That(diagnostic.Stage, Is.EqualTo("RuntimeValidation"));
            Assert.That(diagnostic.DisplayMessage, Does.Contain("Bad style"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("secret-token"));
            Assert.That(diagnostic.TechnicalDetails, Does.Contain("[REDACTED]"));
        }
    }
}
