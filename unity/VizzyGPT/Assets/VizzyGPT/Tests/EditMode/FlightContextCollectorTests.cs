#nullable enable

using System;
using System.Collections.Generic;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Runtime.Flight;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class FlightContextCollectorTests
    {
        [Test]
        public void Collector_bounds_logs_and_telemetry_and_summarizes_latest_min_and_max()
        {
            var now = new DateTime(2026, 7, 22, 12, 0, 0, DateTimeKind.Utc);
            var source = new FakeLogSource();
            var sampleNumber = 0;
            var sampler = new TelemetrySampler(
                timestamp => new TelemetrySample(
                    timestamp,
                    new Dictionary<string, double> { ["altitude"] = sampleNumber++ }),
                () => now.AddSeconds(sampleNumber * 0.5));
            using var collector = new FlightContextCollector(source, sampler, () => now);

            for (var index = 0; index < 250; index++)
            {
                source.Add("log-" + index);
            }

            collector.Tick(100d);
            var snapshot = collector.Snapshot();
            var context = snapshot.BuildContext();

            Assert.That(snapshot.Logs.Count, Is.EqualTo(200));
            Assert.That(snapshot.Logs[0].Message, Is.EqualTo("log-50"));
            Assert.That(snapshot.Logs[199].Message, Is.EqualTo("log-249"));
            Assert.That(snapshot.Telemetry.Count, Is.EqualTo(120));
            Assert.That(context, Does.Contain("altitude:"));
            Assert.That(context, Does.Contain("latest=199"));
            Assert.That(context, Does.Contain("min=80"));
            Assert.That(context, Does.Contain("max=199"));
            Assert.That(context, Does.Contain("log-249"));
            Assert.That(context, Does.Not.Contain("log-49"));
        }

        [Test]
        public void Dispose_unsubscribes_from_the_log_source()
        {
            var source = new FakeLogSource();
            var sampler = new TelemetrySampler(
                timestamp => new TelemetrySample(timestamp, new Dictionary<string, double>()),
                () => DateTime.UtcNow);
            var collector = new FlightContextCollector(source, sampler, () => DateTime.UtcNow);

            Assert.That(source.SubscriberCount, Is.EqualTo(1));
            collector.Dispose();

            Assert.That(source.SubscriberCount, Is.EqualTo(0));
        }

        private sealed class FakeLogSource : IFlightLogSource
        {
            private readonly List<string> messages = new List<string>();
            private Action<string>? messageAdded;

            public IReadOnlyList<string> Messages => messages;

            public int SubscriberCount { get; private set; }

            public event Action<string> MessageAdded
            {
                add
                {
                    messageAdded += value;
                    SubscriberCount++;
                }
                remove
                {
                    messageAdded -= value;
                    SubscriberCount--;
                }
            }

            public void Add(string message)
            {
                messages.Add(message);
                messageAdded?.Invoke(message);
            }
        }
    }
}
