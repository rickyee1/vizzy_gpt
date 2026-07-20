using System;

namespace VizzyGPT.Core.Api
{
    public sealed class AiRequest
    {
        public AiRequest(
            ApiMode mode,
            string prompt,
            string context,
            string model,
            Uri baseUri,
            string apiKey,
            TimeSpan timeout)
        {
            if (!Enum.IsDefined(typeof(ApiMode), mode))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            Prompt = RequireValue(prompt, nameof(prompt));
            Context = NormalizeLineEndings(context ?? throw new ArgumentNullException(nameof(context)));
            Model = RequireValue(model, nameof(model));
            BaseUri = NormalizeBaseUri(baseUri);
            ApiKey = RequireValue(apiKey, nameof(apiKey));
            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
            }

            Mode = mode;
            Timeout = timeout;
        }

        public ApiMode Mode { get; }

        public string Prompt { get; }

        public string Context { get; }

        public string Model { get; }

        public Uri BaseUri { get; }

        public string ApiKey { get; }

        public TimeSpan Timeout { get; }

        private static Uri NormalizeBaseUri(Uri baseUri)
        {
            if (baseUri == null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }

            if (!baseUri.IsAbsoluteUri)
            {
                throw new ArgumentException("The API base URI must be absolute.", nameof(baseUri));
            }

            var isHttps = string.Equals(baseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
            var isHttp = string.Equals(baseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase);
            if (!isHttps && !isHttp)
            {
                throw new ArgumentException("The API base URI must use HTTP or HTTPS.", nameof(baseUri));
            }

            if (isHttp &&
                !string.Equals(baseUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(baseUri.Host, "127.0.0.1", StringComparison.Ordinal) &&
                !string.Equals(baseUri.Host, "::1", StringComparison.Ordinal) &&
                !string.Equals(baseUri.Host, "[::1]", StringComparison.Ordinal))
            {
                throw new ArgumentException("Plain HTTP is permitted only for an exact loopback host.", nameof(baseUri));
            }

            if (baseUri.Host.Length == 0 ||
                baseUri.UserInfo.Length != 0 ||
                baseUri.Query.Length != 0 ||
                baseUri.Fragment.Length != 0)
            {
                throw new ArgumentException("The API base URI contains unsafe components.", nameof(baseUri));
            }

            var normalizedPath = baseUri.AbsolutePath.TrimEnd('/');
            var normalized = baseUri.GetLeftPart(UriPartial.Authority) + normalizedPath;
            return new Uri(normalized, UriKind.Absolute);
        }

        private static string RequireValue(string value, string parameterName)
        {
            if (value == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Value cannot be empty or whitespace.", parameterName);
            }

            return value;
        }

        private static string NormalizeLineEndings(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
