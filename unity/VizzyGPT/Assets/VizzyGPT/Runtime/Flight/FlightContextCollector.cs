#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using ModApi.Flight;
using ModApi.Flight.UI;
using VizzyGPT.Core.Api;
using CoreFlightLogEntry = VizzyGPT.Core.Api.FlightLogEntry;

namespace VizzyGPT.Runtime.Flight
{
    public interface IFlightLogSource
    {
        IReadOnlyList<string> Messages { get; }

        event Action<string> MessageAdded;
    }

    public sealed class FlightContextSnapshot
    {
        public FlightContextSnapshot(
            IReadOnlyList<CoreFlightLogEntry> logs,
            IReadOnlyList<TelemetrySample> telemetry)
        {
            Logs = logs ?? throw new ArgumentNullException(nameof(logs));
            Telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        }

        public IReadOnlyList<CoreFlightLogEntry> Logs { get; }

        public IReadOnlyList<TelemetrySample> Telemetry { get; }

        public string BuildContext()
        {
            return new ContextBuilder().BuildFlightContext(Logs, Telemetry);
        }
    }

    public sealed class FlightContextCollector : IDisposable
    {
        private const int LogCapacity = 200;

        private readonly IFlightLogSource logSource;
        private readonly TelemetrySampler telemetrySampler;
        private readonly Func<DateTime> utcNow;
        private readonly Queue<CoreFlightLogEntry> logs = new Queue<CoreFlightLogEntry>(LogCapacity);
        private bool disposed;

        public FlightContextCollector(
            IFlightLogSource logSource,
            TelemetrySampler telemetrySampler,
            Func<DateTime> utcNow)
        {
            this.logSource = logSource ?? throw new ArgumentNullException(nameof(logSource));
            this.telemetrySampler = telemetrySampler ?? throw new ArgumentNullException(nameof(telemetrySampler));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
            foreach (var message in logSource.Messages.Skip(Math.Max(0, logSource.Messages.Count - LogCapacity)))
            {
                AddLog(message);
            }

            logSource.MessageAdded += AddLog;
        }

        public static FlightContextCollector Create(IFlightScene scene, Func<DateTime> utcNow)
        {
            if (scene == null)
            {
                throw new ArgumentNullException(nameof(scene));
            }

            var sampler = new TelemetrySampler(
                timestamp => CaptureTelemetry(scene, timestamp),
                utcNow);
            return new FlightContextCollector(new JunoFlightLogSource(scene), sampler, utcNow);
        }

        public void Tick(double deltaSeconds)
        {
            if (disposed)
            {
                return;
            }

            telemetrySampler.Tick(deltaSeconds);
        }

        public FlightContextSnapshot Snapshot()
        {
            return new FlightContextSnapshot(logs.ToArray(), telemetrySampler.Samples);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            logSource.MessageAdded -= AddLog;
            disposed = true;
        }

        private void AddLog(string message)
        {
            logs.Enqueue(new CoreFlightLogEntry(utcNow(), message ?? string.Empty));
            while (logs.Count > LogCapacity)
            {
                logs.Dequeue();
            }
        }

        private static TelemetrySample CaptureTelemetry(IFlightScene scene, DateTime timestamp)
        {
            var craft = scene.CraftNode;
            var flightData = craft?.CraftScript?.FlightData;
            var controls = craft?.Controls;
            var metrics = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["universalTime"] = scene.FlightState?.Time ?? 0d,
                ["activeCraftId"] = craft?.NodeId ?? -1,
                ["altitude"] = flightData?.AltitudeAboveSeaLevel ?? craft?.Altitude ?? 0d,
                ["surfaceSpeed"] = flightData?.SurfaceVelocityMagnitude ?? 0d,
                ["verticalSpeed"] = flightData?.VerticalSurfaceVelocity ?? 0d,
                ["mach"] = flightData?.MachNumber ?? 0d,
                ["angleOfAttack"] = flightData?.AngleOfAttack ?? 0d,
                ["pitch"] = flightData?.Pitch ?? 0d,
                ["heading"] = flightData?.Heading ?? 0d,
                ["roll"] = flightData?.BankAngle ?? 0d,
                ["throttle"] = controls?.Throttle ?? 0d
            };
            return new TelemetrySample(timestamp, metrics);
        }

        private sealed class JunoFlightLogSource : IFlightLogSource
        {
            private readonly IFlightLog? flightLog;
            private Action<string>? messageAdded;

            public JunoFlightLogSource(IFlightScene scene)
            {
                flightLog = scene.FlightSceneUI?.FlightLog;
            }

            public IReadOnlyList<string> Messages => flightLog?.LogEntries
                .Select(entry => entry.Text ?? string.Empty)
                .ToArray() ?? Array.Empty<string>();

            public event Action<string> MessageAdded
            {
                add
                {
                    if (messageAdded == null && flightLog != null)
                    {
                        flightLog.LogEntryAdded += OnLogEntryAdded;
                    }

                    messageAdded += value;
                }
                remove
                {
                    messageAdded -= value;
                    if (messageAdded == null && flightLog != null)
                    {
                        flightLog.LogEntryAdded -= OnLogEntryAdded;
                    }
                }
            }

            private void OnLogEntryAdded(ModApi.Flight.UI.FlightLogEntry entry)
            {
                messageAdded?.Invoke(entry?.Text ?? string.Empty);
            }
        }
    }
}
