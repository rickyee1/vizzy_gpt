using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VizzyGPT.Core.Validation
{
    public enum ValidationSeverity
    {
        Error,
        Warning
    }

    public sealed class ValidationIssue
    {
        public ValidationIssue(ValidationSeverity severity, string code, string message, string? path = null)
        {
            if (!Enum.IsDefined(typeof(ValidationSeverity), severity))
            {
                throw new ArgumentOutOfRangeException(nameof(severity));
            }

            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Validation issue code must be non-whitespace.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("Validation issue message must be non-whitespace.", nameof(message));
            }

            if (path != null && path.Length == 0)
            {
                throw new ArgumentException("Validation issue path cannot be empty.", nameof(path));
            }

            Severity = severity;
            Code = code;
            Message = message;
            Path = path;
        }

        public ValidationSeverity Severity { get; }

        public string Code { get; }

        public string Message { get; }

        public string? Path { get; }
    }

    public sealed class ValidationReport
    {
        public ValidationReport(IReadOnlyList<ValidationIssue> issues)
        {
            if (issues == null)
            {
                throw new ArgumentNullException(nameof(issues));
            }

            if (issues.Any(issue => issue == null))
            {
                throw new ArgumentException("Validation issues cannot contain null entries.", nameof(issues));
            }

            var issueCopy = issues.ToArray();
            Issues = new ReadOnlyCollection<ValidationIssue>(issueCopy);
            Errors = new ReadOnlyCollection<ValidationIssue>(
                issueCopy.Where(issue => issue.Severity == ValidationSeverity.Error).ToArray());
            Warnings = new ReadOnlyCollection<ValidationIssue>(
                issueCopy.Where(issue => issue.Severity == ValidationSeverity.Warning).ToArray());
        }

        public bool IsValid => Errors.Count == 0;

        public IReadOnlyList<ValidationIssue> Issues { get; }

        public IReadOnlyList<ValidationIssue> Errors { get; }

        public IReadOnlyList<ValidationIssue> Warnings { get; }
    }
}
