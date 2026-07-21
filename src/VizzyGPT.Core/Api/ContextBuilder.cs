using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using VizzyGPT.Core.Patching;
using VizzyGPT.Core.Programs;
using VizzyGPT.Core.Security;

namespace VizzyGPT.Core.Api
{
    public sealed class FlightLogEntry
    {
        public FlightLogEntry(DateTime timestampUtc, string message)
        {
            TimestampUtc = NormalizeUtc(timestampUtc);
            Message = message ?? throw new ArgumentNullException(nameof(message));
        }

        public DateTime TimestampUtc { get; }

        public string Message { get; }

        private static DateTime NormalizeUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
            {
                return value;
            }

            return value.Kind == DateTimeKind.Local
                ? value.ToUniversalTime()
                : DateTime.SpecifyKind(value, DateTimeKind.Utc);
        }
    }

    public sealed class TelemetrySample
    {
        public TelemetrySample(DateTime timestampUtc, IReadOnlyDictionary<string, double> metrics)
        {
            if (metrics == null)
            {
                throw new ArgumentNullException(nameof(metrics));
            }

            var copy = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var metric in metrics)
            {
                if (metric.Key == null)
                {
                    throw new ArgumentException("Metric names cannot be null.", nameof(metrics));
                }

                copy.Add(metric.Key, metric.Value);
            }

            TimestampUtc = timestampUtc.Kind == DateTimeKind.Utc
                ? timestampUtc
                : timestampUtc.Kind == DateTimeKind.Local
                    ? timestampUtc.ToUniversalTime()
                    : DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc);
            Metrics = new ReadOnlyDictionary<string, double>(copy);
        }

        public DateTime TimestampUtc { get; }

        public IReadOnlyDictionary<string, double> Metrics { get; }
    }

    public sealed class ContextBuilder
    {
        private const int MaximumContextBytes = 64 * 1024;
        private const int MaximumLogs = 200;
        private const int MaximumTelemetrySamples = 120;
        private const string TruncationMarker = "\n[TRUNCATED]";

        private readonly string? configuredApiKey;

        public ContextBuilder(string? configuredApiKey = null)
        {
            this.configuredApiKey = configuredApiKey;
        }

        public string BuildEditorContext(
            VizzyProgramDocument document,
            string declarations,
            NodeSelector? selection)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            if (declarations == null)
            {
                throw new ArgumentNullException(nameof(declarations));
            }

            var canonicalXml = document.ToXml();
            var complete = "EDITOR CONTEXT\nDeclarations:\n" + declarations + "\nProgram XML:\n" + canonicalXml;
            var safeComplete = Sanitize(complete);
            var selectionBuilder = new StringBuilder();
            AppendSelection(selectionBuilder, document, selection, out var ambiguousSelection);
            if (!ambiguousSelection && Encoding.UTF8.GetByteCount(safeComplete) <= MaximumContextBytes)
            {
                return safeComplete;
            }

            var summaryBuilder = new StringBuilder("Root summaries:");
            AppendRootSummaries(summaryBuilder, document.Root);
            return ComposeBoundedSections(
                "EDITOR CONTEXT",
                Sanitize("Declarations:\n" + declarations),
                Sanitize(summaryBuilder.ToString()),
                Sanitize("Selected subtree:\n" + selectionBuilder));
        }

        public string BuildFlightContext(
            IReadOnlyList<FlightLogEntry> logs,
            IReadOnlyList<TelemetrySample> telemetry)
        {
            if (logs == null)
            {
                throw new ArgumentNullException(nameof(logs));
            }

            if (telemetry == null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }

            if (logs.Any(log => log == null))
            {
                throw new ArgumentException("Logs cannot contain null entries.", nameof(logs));
            }

            if (telemetry.Any(sample => sample == null))
            {
                throw new ArgumentException("Telemetry cannot contain null entries.", nameof(telemetry));
            }

            var recentLogs = logs
                .Select((entry, index) => (Entry: entry, Index: index))
                .OrderBy(item => item.Entry.TimestampUtc)
                .ThenBy(item => item.Index)
                .ToArray();
            recentLogs = recentLogs
                .Skip(Math.Max(0, recentLogs.Length - MaximumLogs))
                .ToArray();

            var recentTelemetry = telemetry
                .Select((sample, index) => (Sample: sample, Index: index))
                .OrderBy(item => item.Sample.TimestampUtc)
                .ThenBy(item => item.Index)
                .ToArray();
            recentTelemetry = recentTelemetry
                .Skip(Math.Max(0, recentTelemetry.Length - MaximumTelemetrySamples))
                .ToArray();

            var telemetryBuilder = new StringBuilder("Telemetry summaries:");
            AppendTelemetrySummaries(telemetryBuilder, recentTelemetry.Select(item => item.Sample).ToArray());
            var safeTelemetry = Sanitize(telemetryBuilder.ToString());
            var safeLogLines = new List<string>();
            foreach (var item in recentLogs)
            {
                safeLogLines.Add(Sanitize(
                    item.Entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture) +
                    " " + item.Entry.Message));
            }

            var full = "FLIGHT CONTEXT\n" + safeTelemetry + "\nRecent logs:" +
                (safeLogLines.Count == 0 ? string.Empty : "\n" + string.Join("\n", safeLogLines));
            if (Encoding.UTF8.GetByteCount(full) <= MaximumContextBytes)
            {
                return full;
            }

            const int telemetryBudget = 16 * 1024;
            var boundedTelemetry = BoundToBytes(safeTelemetry, telemetryBudget);
            var fixedBytes = Encoding.UTF8.GetByteCount("FLIGHT CONTEXT\n\n") +
                Encoding.UTF8.GetByteCount(boundedTelemetry);
            var logsBudget = MaximumContextBytes - fixedBytes;
            var boundedLogs = BuildNewestLogsSection(safeLogLines, logsBudget);
            return "FLIGHT CONTEXT\n" + boundedTelemetry + "\n" + boundedLogs;
        }

        private static void AppendRootSummaries(StringBuilder builder, XElement root)
        {
            var instructions = root.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Instructions", StringComparison.Ordinal));
            var expressions = root.Elements().FirstOrDefault(element =>
                string.Equals(element.Name.LocalName, "Expressions", StringComparison.Ordinal));
            var summaryElements = Enumerable.Empty<XElement>();
            if (instructions != null)
            {
                summaryElements = summaryElements.Concat(instructions.Elements().Where(element =>
                    string.Equals(element.Name.LocalName, "Event", StringComparison.Ordinal)));
            }

            if (expressions != null)
            {
                summaryElements = summaryElements.Concat(expressions.Elements().Where(element =>
                    string.Equals(element.Name.LocalName, "CustomNode", StringComparison.Ordinal)));
            }

            foreach (var element in summaryElements)
            {
                builder.Append('\n').Append(element.Name.LocalName);
                foreach (var attribute in element.Attributes().OrderBy(attribute => attribute.Name.ToString(), StringComparer.Ordinal))
                {
                    builder.Append(' ')
                        .Append(attribute.Name.LocalName)
                        .Append('=')
                        .Append(attribute.Value);
                }
            }
        }

        private static void AppendSelection(
            StringBuilder builder,
            VizzyProgramDocument document,
            NodeSelector? selection,
            out bool ambiguous)
        {
            ambiguous = false;
            if (selection == null)
            {
                builder.Append("Selection: none");
                return;
            }

            XElement? selected = null;
            if (selection.Id.HasValue)
            {
                var matches = document.Root.DescendantsAndSelf().Where(element =>
                {
                    var id = element.Attribute("id")?.Value;
                    return int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                        parsed == selection.Id.Value;
                }).Take(2).ToArray();
                if (matches.Length == 0)
                {
                    builder.Append("Selection: missing");
                    return;
                }

                if (matches.Length > 1)
                {
                    ambiguous = true;
                    builder.Append("Selection: ambiguous");
                    return;
                }

                selected = matches[0];
            }
            else if (selection.Path != null)
            {
                selected = document.FindByPath(selection.Path);
                if (selected == null)
                {
                    builder.Append("Selection: missing");
                    return;
                }
            }

            builder.Append("Selection: matched\n").Append(CanonicalXml.Write(selected!));
        }

        private static void AppendTelemetrySummaries(
            StringBuilder builder,
            IReadOnlyList<TelemetrySample> samples)
        {
            var metricNames = samples
                .SelectMany(sample => sample.Metrics.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(name => name, StringComparer.Ordinal);
            foreach (var metricName in metricNames)
            {
                var values = samples
                    .Where(sample => sample.Metrics.ContainsKey(metricName))
                    .Select(sample => sample.Metrics[metricName])
                    .Where(value => !double.IsNaN(value) && !double.IsInfinity(value))
                    .ToArray();
                if (values.Length == 0)
                {
                    continue;
                }

                builder.Append('\n')
                    .Append(metricName)
                    .Append(": min=")
                    .Append(values.Min().ToString("G17", CultureInfo.InvariantCulture))
                    .Append(" max=")
                    .Append(values.Max().ToString("G17", CultureInfo.InvariantCulture))
                    .Append(" latest=")
                    .Append(values[values.Length - 1].ToString("G17", CultureInfo.InvariantCulture));
            }
        }

        private string Sanitize(string value)
        {
            var normalized = value.Replace("\r\n", "\n").Replace('\r', '\n');
            return SecretRedactor.Redact(normalized, configuredApiKey);
        }

        private static string ComposeBoundedSections(string header, params string[] sections)
        {
            var complete = header + "\n" + string.Join("\n", sections);
            if (Encoding.UTF8.GetByteCount(complete) <= MaximumContextBytes)
            {
                return complete;
            }

            var separators = Encoding.UTF8.GetByteCount(header) + sections.Length;
            var sectionBudget = (MaximumContextBytes - separators) / sections.Length;
            var bounded = sections.Select(section => BoundToBytes(section, sectionBudget)).ToArray();
            return header + "\n" + string.Join("\n", bounded);
        }

        private static string BuildNewestLogsSection(IReadOnlyList<string> lines, int byteBudget)
        {
            const string header = "Recent logs:";
            if (lines.Count == 0)
            {
                return header;
            }

            var selected = new List<string>();
            var usedBytes = Encoding.UTF8.GetByteCount(header) + Encoding.UTF8.GetByteCount(TruncationMarker);
            for (var index = lines.Count - 1; index >= 0; index--)
            {
                var lineBytes = 1 + Encoding.UTF8.GetByteCount(lines[index]);
                if (usedBytes + lineBytes > byteBudget)
                {
                    break;
                }

                selected.Add(lines[index]);
                usedBytes += lineBytes;
            }

            selected.Reverse();
            var omitted = selected.Count < lines.Count;
            var builder = new StringBuilder(header);
            if (omitted)
            {
                builder.Append(TruncationMarker);
            }

            if (selected.Count == 0)
            {
                var remaining = byteBudget - Encoding.UTF8.GetByteCount(builder.ToString()) - 1;
                if (remaining > 0)
                {
                    builder.Append('\n').Append(BoundToBytes(lines[lines.Count - 1], remaining));
                }
            }
            else
            {
                foreach (var line in selected)
                {
                    builder.Append('\n').Append(line);
                }
            }

            return BoundToBytes(builder.ToString(), byteBudget);
        }

        private static string BoundToBytes(string value, int maximumBytes)
        {
            if (Encoding.UTF8.GetByteCount(value) <= maximumBytes)
            {
                return value;
            }

            if (maximumBytes <= Encoding.UTF8.GetByteCount(TruncationMarker))
            {
                return string.Empty;
            }

            var prefixBudget = maximumBytes - Encoding.UTF8.GetByteCount(TruncationMarker);
            var low = 0;
            var high = value.Length;
            while (low < high)
            {
                var middle = low + ((high - low + 1) / 2);
                if (Encoding.UTF8.GetByteCount(value, 0, middle) <= prefixBudget)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            var length = low;
            if (length > 0 && length < value.Length &&
                char.IsHighSurrogate(value[length - 1]) && char.IsLowSurrogate(value[length]))
            {
                length--;
            }

            var placeholderStart = value.LastIndexOf("[REDACTED]", Math.Max(0, length - 1), StringComparison.Ordinal);
            if (placeholderStart >= 0 && placeholderStart + "[REDACTED]".Length > length)
            {
                length = placeholderStart;
            }

            while (length > 0 && Encoding.UTF8.GetByteCount(value, 0, length) > prefixBudget)
            {
                length--;
            }

            return value.Substring(0, length) + TruncationMarker;
        }
    }
}
