using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;
using VizzyGPT.Core.Api;

namespace VizzyGPT.Runtime.Api
{
    public sealed class UnityWebRequestTransport : IAiTransport
    {
        public async Task<HttpTransportResponse> SendAsync(
            HttpTransportRequest request,
            CancellationToken cancellationToken)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            cancellationToken.ThrowIfCancellationRequested();

            using (var webRequest = new UnityWebRequest(request.Uri, request.Method))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(request.Body);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.timeout = ToTimeoutSeconds(request.Timeout);
                foreach (var header in request.Headers)
                {
                    webRequest.SetRequestHeader(header.Key, header.Value);
                }

                if (!request.Headers.ContainsKey("Content-Type"))
                {
                    webRequest.SetRequestHeader("Content-Type", "application/json");
                }

                using (cancellationToken.Register(webRequest.Abort))
                {
                    var operation = webRequest.SendWebRequest();
                    while (!operation.isDone)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await Task.Yield();
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (webRequest.responseCode < 100)
                {
                    throw CreateTransientFailure(webRequest.error);
                }

                if (webRequest.result == UnityWebRequest.Result.ConnectionError ||
                    webRequest.result == UnityWebRequest.Result.DataProcessingError)
                {
                    throw CreateTransientFailure(webRequest.error);
                }

                return new HttpTransportResponse(
                    checked((int)webRequest.responseCode),
                    webRequest.downloadHandler.data ?? Array.Empty<byte>());
            }
        }

        private static TransientAiTransportException CreateTransientFailure(string error)
        {
            return new TransientAiTransportException(
                "The AI service closed the connection before returning a complete HTTP response (" +
                DescribeError(error) + ").");
        }

        private static string DescribeError(string error)
        {
            if (string.IsNullOrWhiteSpace(error))
            {
                return "network connection ended unexpectedly";
            }

            var value = error.ToLowerInvariant();
            if (value.Contains("empty reply"))
            {
                return "empty reply from server";
            }

            if (value.Contains("timed out") || value.Contains("timeout"))
            {
                return "connection timed out";
            }

            if (value.Contains("resolve") || value.Contains("dns"))
            {
                return "DNS lookup failed";
            }

            if (value.Contains("certificate") || value.Contains("ssl") || value.Contains("tls"))
            {
                return "TLS certificate validation failed";
            }

            if (value.Contains("reset"))
            {
                return "connection reset by server";
            }

            return "network connection ended unexpectedly";
        }

        private static int ToTimeoutSeconds(TimeSpan timeout)
        {
            return Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalSeconds)));
        }
    }
}
