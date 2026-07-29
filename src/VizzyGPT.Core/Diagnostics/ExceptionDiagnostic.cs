using System;
using System.Reflection;
using System.Text.RegularExpressions;

namespace VizzyGPT.Core.Diagnostics
{
    public sealed class ExceptionDiagnostic
    {
        private static readonly Regex Bearer = new Regex(
            @"(?i)(authorization\s*:\s*bearer\s+|bearer\s+)[^\s,;]+",
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

            var safe = Bearer.Replace(root.Message ?? root.GetType().Name, "$1[REDACTED]");
            return new ExceptionDiagnostic(root.GetType().Name, stage, safe, root.GetType().FullName + ": " + safe);
        }
    }
}
