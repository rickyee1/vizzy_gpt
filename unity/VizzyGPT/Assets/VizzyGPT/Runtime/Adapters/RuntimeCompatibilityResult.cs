#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace VizzyGPT.Runtime.Adapters
{
    public enum RuntimeCompatibilityKind
    {
        Compatible,
        EditorUnavailable,
        AmbiguousContract
    }

    public sealed class RuntimeCompatibilityResult
    {
        private const string UnavailableDiagnostic =
            "Vizzy editor contract unavailable: expected one public FlightProgram member with a refresh method.";

        private RuntimeCompatibilityResult(
            RuntimeCompatibilityKind kind,
            string diagnostic,
            IReadOnlyList<string> memberSummaries)
        {
            Kind = kind;
            Diagnostic = diagnostic;
            MemberSummaries = memberSummaries;
        }

        public RuntimeCompatibilityKind Kind { get; }

        public string Diagnostic { get; }

        public IReadOnlyList<string> MemberSummaries { get; }

        public bool CanModify => Kind == RuntimeCompatibilityKind.Compatible;

        public static RuntimeCompatibilityResult Compatible(string memberSummary)
        {
            var summary = RequireSummary(memberSummary);
            return new RuntimeCompatibilityResult(
                RuntimeCompatibilityKind.Compatible,
                "Vizzy editor contract compatible: " + summary + ".",
                new[] { summary });
        }

        public static RuntimeCompatibilityResult EditorUnavailable()
        {
            return new RuntimeCompatibilityResult(
                RuntimeCompatibilityKind.EditorUnavailable,
                UnavailableDiagnostic,
                Array.Empty<string>());
        }

        public static RuntimeCompatibilityResult AmbiguousContract(params string[] memberSummaries)
        {
            if (memberSummaries == null)
            {
                throw new ArgumentNullException(nameof(memberSummaries));
            }

            var summaries = memberSummaries
                .Select(RequireSummary)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            if (summaries.Length < 2)
            {
                throw new ArgumentException(
                    "An ambiguous contract requires at least two distinct member summaries.",
                    nameof(memberSummaries));
            }

            return new RuntimeCompatibilityResult(
                RuntimeCompatibilityKind.AmbiguousContract,
                "Vizzy editor contract ambiguous: " + string.Join(", ", summaries) + ".",
                summaries);
        }

        private static string RequireSummary(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? throw new ArgumentException("A compatibility member summary must be non-whitespace.")
                : value.Trim();
        }
    }
}
