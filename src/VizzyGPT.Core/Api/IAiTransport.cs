using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace VizzyGPT.Core.Api
{
    public interface IAiTransport
    {
        Task<HttpTransportResponse> SendAsync(
            HttpTransportRequest request,
            CancellationToken cancellationToken);
    }

    public sealed class HttpTransportRequest
    {
        private readonly byte[] body;

        public HttpTransportRequest(
            string method,
            Uri uri,
            IReadOnlyDictionary<string, string> headers,
            byte[] body,
            TimeSpan timeout)
        {
            if (string.IsNullOrWhiteSpace(method))
            {
                throw new ArgumentException("HTTP method is required.", nameof(method));
            }

            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (!uri.IsAbsoluteUri)
            {
                throw new ArgumentException("Transport URI must be absolute.", nameof(uri));
            }

            if (headers == null)
            {
                throw new ArgumentNullException(nameof(headers));
            }

            if (body == null)
            {
                throw new ArgumentNullException(nameof(body));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be positive.");
            }

            var headerCopy = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var header in headers)
            {
                if (header.Key == null || header.Value == null)
                {
                    throw new ArgumentException("Headers cannot contain null keys or values.", nameof(headers));
                }

                headerCopy.Add(header.Key, header.Value);
            }

            Method = method;
            Uri = uri;
            Headers = new ReadOnlyDictionary<string, string>(headerCopy);
            this.body = body.ToArray();
            Timeout = timeout;
        }

        public string Method { get; }

        public Uri Uri { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] Body => body.ToArray();

        public TimeSpan Timeout { get; }
    }

    public sealed class HttpTransportResponse
    {
        private readonly byte[] body;

        public HttpTransportResponse(int statusCode, byte[] body)
        {
            if (statusCode < 100 || statusCode > 999)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode));
            }

            this.body = body?.ToArray() ?? throw new ArgumentNullException(nameof(body));
            StatusCode = statusCode;
        }

        public int StatusCode { get; }

        public byte[] Body => body.ToArray();
    }
}
