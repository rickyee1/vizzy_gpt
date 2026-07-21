using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using VizzyGPT.Core.Api;

namespace VizzyGPT.Core.Storage
{
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NonSecretSettings
    {
        [JsonConstructor]
        public NonSecretSettings(string baseUrl, ApiMode mode, string model, int timeoutSeconds)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                throw new ArgumentException("Base URL must be non-whitespace.", nameof(baseUrl));
            }

            if (!Enum.IsDefined(typeof(ApiMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new ArgumentException("Model must be non-whitespace.", nameof(model));
            }

            if (timeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds));
            }

            BaseUrl = baseUrl;
            Mode = mode;
            Model = model;
            TimeoutSeconds = timeoutSeconds;
        }

        [JsonProperty("baseUrl", Required = Required.Always)]
        public string BaseUrl { get; }

        [JsonProperty("mode", Required = Required.Always)]
        [JsonConverter(typeof(StringEnumConverter))]
        public ApiMode Mode { get; }

        [JsonProperty("model", Required = Required.Always)]
        public string Model { get; }

        [JsonProperty("timeoutSeconds", Required = Required.Always)]
        public int TimeoutSeconds { get; }
    }
}
