using System;
using System.Reflection;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using VizzyGPT.Core.Security;

namespace VizzyGPT.Core.Diagnostics
{
    public sealed class ExceptionDiagnostic
    {
        private const int MaximumMessageLength = 512;
        private const string RequestContextPlaceholder = "[REDACTED REQUEST CONTEXT]";
        private const string StructuredPayloadPlaceholder = "[REDACTED STRUCTURED PAYLOAD]";
        private const string TruncatedPlaceholder = "[TRUNCATED]";

        private static readonly Regex AuthorizationValue = new Regex(
            @"(?i)(authorization\s*[:=]\s*)[^\r\n,;]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex BareBearer = new Regex(
            @"(?i)(bearer\s+)[^\s,;]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex ApiKeyAssignment = new Regex(
            @"(?i)(api[\s_-]*key\s*[:=]\s*)[^\s,;]+",
            RegexOptions.CultureInvariant);

        private static readonly Regex JsonPayload = new Regex(
            @"(?s)[\{\[]\s*(?=[\{\[\""0-9tfn-]).*",
            RegexOptions.CultureInvariant);

        private static readonly Regex XmlPayload = new Regex(
            @"(?is)<(?:\?xml\b.*|[A-Za-z_][A-Za-z0-9_.:-]*(?:\s[^>]*)?/?>.*)",
            RegexOptions.CultureInvariant);

        private static readonly Regex RequestContext = new Regex(
            @"(?is)\b(?:request|response)(?:\s+(?:context|details|headers?|body))?\s*[:=]\s*.*",
            RegexOptions.CultureInvariant);

        private static readonly Regex PlaintextBody = new Regex(
            @"(?is)\bbody\s*[:=](?!\s*[\{\[<])\s*.*",
            RegexOptions.CultureInvariant);

        private ExceptionDiagnostic(string code, string stage, string displayMessage, string technicalDetails)
        {
            Code = code;
            Stage = stage;
            DisplayMessage = displayMessage;
            TechnicalDetails = technicalDetails;
        }

        public string Code { get; }

        public string Stage { get; }

        public string DisplayMessage { get; }

        public string TechnicalDetails { get; }

        public static ExceptionDiagnostic From(Exception exception, string stage)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (string.IsNullOrWhiteSpace(stage))
            {
                throw new ArgumentException("Stage is required.", nameof(stage));
            }

            var root = exception;
            while (root.InnerException != null &&
                (root is TargetInvocationException ||
                 root is TypeInitializationException ||
                 root is AggregateException aggregate && aggregate.InnerExceptions.Count == 1))
            {
                root = root.InnerException;
            }

            var safe = Sanitize(root.Message ?? root.GetType().Name);
            var technicalDetails = Bound((root.GetType().FullName ?? root.GetType().Name) + ": " + safe);
            return new ExceptionDiagnostic(root.GetType().Name, stage, safe, technicalDetails);
        }

        private static string Sanitize(string message)
        {
            var safe = SecretRedactor.Redact(message);
            if (IsTopLevelJson(safe))
            {
                return StructuredPayloadPlaceholder;
            }

            safe = AuthorizationValue.Replace(safe, "$1[REDACTED]");
            safe = BareBearer.Replace(safe, "$1[REDACTED]");
            safe = ApiKeyAssignment.Replace(safe, "$1[REDACTED]");
            safe = RequestContext.Replace(safe, RequestContextPlaceholder);
            safe = PlaintextBody.Replace(safe, RequestContextPlaceholder);
            safe = JsonPayload.Replace(safe, StructuredPayloadPlaceholder);
            safe = XmlPayload.Replace(safe, StructuredPayloadPlaceholder);
            return Bound(safe);
        }

        private static bool IsTopLevelJson(string value)
        {
            try
            {
                JToken.Parse(value);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Bound(string value)
        {
            if (value.Length <= MaximumMessageLength)
            {
                return value;
            }

            return value.Substring(0, MaximumMessageLength - TruncatedPlaceholder.Length) + TruncatedPlaceholder;
        }
    }
}
