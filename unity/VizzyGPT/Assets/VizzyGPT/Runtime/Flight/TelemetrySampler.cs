#nullable enable

using System;
using System.Collections.Generic;
using VizzyGPT.Core.Api;

namespace VizzyGPT.Runtime.Flight
{
    public sealed class TelemetrySampler
    {
        private const double SampleIntervalSeconds = 0.5d;
        private const int Capacity = 120;

        private readonly Func<DateTime, TelemetrySample> capture;
        private readonly Func<DateTime> utcNow;
        private readonly Queue<TelemetrySample> samples = new Queue<TelemetrySample>(Capacity);
        private double elapsedSeconds;

        public TelemetrySampler(
            Func<DateTime, TelemetrySample> capture,
            Func<DateTime> utcNow)
        {
            this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
            this.utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
        }

        public IReadOnlyList<TelemetrySample> Samples => samples.ToArray();

        public void Tick(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            elapsedSeconds += deltaSeconds;
            while (elapsedSeconds >= SampleIntervalSeconds)
            {
                elapsedSeconds -= SampleIntervalSeconds;
                samples.Enqueue(capture(utcNow()));
                while (samples.Count > Capacity)
                {
                    samples.Dequeue();
                }
            }
        }
    }
}
