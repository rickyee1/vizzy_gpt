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
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("secret-token"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("secret-token"));
            Assert.That(diagnostic.TechnicalDetails, Does.Contain("[REDACTED]"));
        }

        [Test]
        public void From_unwraps_type_initialization_exceptions()
        {
            var wrapped = new TypeInitializationException(
                "RuntimeType",
                new InvalidOperationException("Inner runtime failure."));

            var diagnostic = ExceptionDiagnostic.From(wrapped, "RuntimeValidation");

            Assert.That(diagnostic.Code, Is.EqualTo("InvalidOperationException"));
            Assert.That(diagnostic.DisplayMessage, Is.EqualTo("Inner runtime failure."));
        }

        [Test]
        public void From_unwraps_single_inner_aggregate_exceptions()
        {
            var wrapped = new AggregateException(new InvalidOperationException("Inner aggregate failure."));

            var diagnostic = ExceptionDiagnostic.From(wrapped, "RuntimeValidation");

            Assert.That(diagnostic.Code, Is.EqualTo("InvalidOperationException"));
            Assert.That(diagnostic.DisplayMessage, Is.EqualTo("Inner aggregate failure."));
        }

        [Test]
        public void From_redacts_credentials_and_replaces_structured_payloads_in_both_messages()
        {
            var exception = new InvalidOperationException(
                "Request failed. Authorization: Basic basic-secret; api_key=assignment-secret; " +
                "Bearer bearer-secret. Body: {\"api_key\":\"json-secret\",\"input\":\"sensitive body\"}");

            var diagnostic = ExceptionDiagnostic.From(exception, "RuntimeValidation");

            AssertSanitized(diagnostic.DisplayMessage);
            AssertSanitized(diagnostic.TechnicalDetails);
            Assert.That(diagnostic.DisplayMessage, Does.Contain("[REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.Contain("[REDACTED STRUCTURED PAYLOAD]"));
        }

        [TestCase("<Program><Instructions><Log text='sensitive xml body' /></Instructions></Program>")]
        [TestCase("{\"model\":\"gpt-test\",\"input\":\"sensitive json body\"}")]
        public void From_replaces_full_structured_payloads_in_both_messages(string payload)
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(payload),
                "RuntimeValidation");

            Assert.That(diagnostic.DisplayMessage, Is.EqualTo("[REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.EndWith(": [REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("sensitive"));
        }

        [TestCase("<!-- sensitive-prolog --><Program />")]
        [TestCase("<!DOCTYPE Program [<!ELEMENT Program ANY>]><Program />")]
        public void From_replaces_valid_xml_with_comments_or_doctype_prefixes(string payload)
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(payload),
                "RuntimeValidation");

            Assert.That(diagnostic.DisplayMessage, Is.EqualTo("[REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.EndWith(": [REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("sensitive-prolog"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("DOCTYPE"));
        }

        [Test]
        public void From_bounds_non_structured_messages_in_both_outputs()
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(new string('x', 1024)),
                "RuntimeValidation");

            Assert.That(diagnostic.DisplayMessage.Length, Is.LessThanOrEqualTo(512));
            Assert.That(diagnostic.TechnicalDetails.Length, Is.LessThanOrEqualTo(512));
            Assert.That(diagnostic.DisplayMessage, Does.EndWith("[TRUNCATED]"));
            Assert.That(diagnostic.TechnicalDetails, Does.EndWith("[TRUNCATED]"));
        }

        [TestCase("\"sensitive string value\"")]
        [TestCase("true")]
        [TestCase("42")]
        [TestCase("null")]
        public void From_replaces_all_valid_top_level_json_values(string payload)
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(payload),
                "RuntimeValidation");

            Assert.That(diagnostic.DisplayMessage, Is.EqualTo("[REDACTED STRUCTURED PAYLOAD]"));
            Assert.That(diagnostic.TechnicalDetails, Does.EndWith(": [REDACTED STRUCTURED PAYLOAD]"));
        }

        [Test]
        public void From_removes_equals_form_authorization_and_plaintext_request_context()
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(
                    "HTTP 401. Authorization=Basic equals-secret; " +
                    "Request context: endpoint=/v1/responses; Body: plaintext response"),
                "RuntimeValidation");

            Assert.That(diagnostic.Code, Is.EqualTo("InvalidOperationException"));
            Assert.That(diagnostic.Stage, Is.EqualTo("RuntimeValidation"));
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("equals-secret"));
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("endpoint=/v1/responses"));
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("plaintext response"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("equals-secret"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("endpoint=/v1/responses"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("plaintext response"));
            Assert.That(diagnostic.DisplayMessage, Does.Contain("[REDACTED REQUEST CONTEXT]"));
            Assert.That(diagnostic.TechnicalDetails, Does.Contain("[REDACTED REQUEST CONTEXT]"));
        }

        [Test]
        public void From_removes_plain_request_equals_context_and_body()
        {
            var diagnostic = ExceptionDiagnostic.From(
                new InvalidOperationException(
                    "HTTP 401. Authorization=Basic request-secret; " +
                    "Request=/v1/responses; Body=plaintext request response"),
                "RuntimeValidation");

            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("request-secret"));
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("/v1/responses"));
            Assert.That(diagnostic.DisplayMessage, Does.Not.Contain("plaintext request response"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("request-secret"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("/v1/responses"));
            Assert.That(diagnostic.TechnicalDetails, Does.Not.Contain("plaintext request response"));
            Assert.That(diagnostic.DisplayMessage, Does.Contain("[REDACTED REQUEST CONTEXT]"));
        }

        private static void AssertSanitized(string value)
        {
            Assert.That(value, Does.Not.Contain("basic-secret"));
            Assert.That(value, Does.Not.Contain("assignment-secret"));
            Assert.That(value, Does.Not.Contain("bearer-secret"));
            Assert.That(value, Does.Not.Contain("json-secret"));
            Assert.That(value, Does.Not.Contain("sensitive body"));
            Assert.That(value, Does.Contain("[REDACTED]"));
            Assert.That(value.Length, Is.LessThanOrEqualTo(512));
        }
    }
}
