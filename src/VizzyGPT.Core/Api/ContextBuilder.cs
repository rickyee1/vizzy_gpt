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
            var preservedSourceBytes = Encoding.UTF8.GetByteCount(
                document.Root.ToString(SaveOptions.DisableFormatting));
            if (preservedSourceBytes <= MaximumContextBytes &&
                Encoding.UTF8.GetByteCount(safeComplete) <= MaximumContextBytes)
            {
                return safeComplete;
            }

            var builder = new StringBuilder();
            builder.Append("EDITOR CONTEXT\nDeclarations:\n").Append(declarations);
            builder.Append("\nRoot summaries:");
            AppendRootSummaries(builder, document.Root);
            builder.Append("\nSelected subtree:\n");
            AppendSelection(builder, document, selection);
            return Bound(Sanitize(builder.ToString()));
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

            var builder = new StringBuilder("FLIGHT CONTEXT\nTelemetry summaries:");
            AppendTelemetrySummaries(builder, recentTelemetry.Select(item => item.Sample).ToArray());
            builder.Append("\nRecent logs:");
            foreach (var item in recentLogs)
            {
                builder.Append('\n')
                    .Append(item.Entry.TimestampUtc.ToString("O", CultureInfo.InvariantCulture))
                    .Append(' ')
                    .Append(item.Entry.Message);
            }

            return Bound(Sanitize(builder.ToString()));
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
            NodeSelector? selection)
        {
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
                    .ToArray();
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

        private static string Bound(string value)
        {
            if (Encoding.UTF8.GetByteCount(value) <= MaximumContextBytes)
            {
                return value;
            }

            var prefixBudget = MaximumContextBytes - Encoding.UTF8.GetByteCount(TruncationMarker);
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
