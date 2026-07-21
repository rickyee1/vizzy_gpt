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

            if (isHttp && !HasExactLoopbackAuthority(baseUri))
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

        private static bool HasExactLoopbackAuthority(Uri baseUri)
        {
            var original = baseUri.OriginalString;
            var schemeSeparator = original.IndexOf("://", StringComparison.Ordinal);
            if (schemeSeparator < 0)
            {
                return false;
            }

            var authorityStart = schemeSeparator + 3;
            var authorityEnd = original.IndexOfAny(new[] { '/', '?', '#' }, authorityStart);
            if (authorityEnd < 0)
            {
                authorityEnd = original.Length;
            }

            var authority = original.Substring(authorityStart, authorityEnd - authorityStart);
            if (authority.Length == 0 || authority.IndexOf('@') >= 0)
            {
                return false;
            }

            string host;
            string port;
            if (authority[0] == '[')
            {
                var closeBracket = authority.IndexOf(']');
                if (closeBracket < 0)
                {
                    return false;
                }

                host = authority.Substring(0, closeBracket + 1);
                port = authority.Substring(closeBracket + 1);
                if (!string.Equals(host, "[::1]", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else
            {
                var colon = authority.LastIndexOf(':');
                host = colon >= 0 ? authority.Substring(0, colon) : authority;
                port = colon >= 0 ? authority.Substring(colon) : string.Empty;
                if (!string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(host, "127.0.0.1", StringComparison.Ordinal))
                {
                    return false;
                }
            }

            if (port.Length == 0)
            {
                return true;
            }

            if (port.Length == 1 || port[0] != ':')
            {
                return false;
            }

            for (var index = 1; index < port.Length; index++)
            {
                if (port[index] < '0' || port[index] > '9')
                {
                    return false;
                }
            }

            return true;
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
