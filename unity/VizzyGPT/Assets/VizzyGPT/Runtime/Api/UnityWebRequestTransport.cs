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
                    throw new InvalidOperationException(
                        "HTTP request did not receive a response: " + (webRequest.error ?? "unknown error"));
                }

                return new HttpTransportResponse(
                    checked((int)webRequest.responseCode),
                    webRequest.downloadHandler.data ?? Array.Empty<byte>());
            }
        }

        private static int ToTimeoutSeconds(TimeSpan timeout)
        {
            return Math.Max(1, (int)Math.Min(int.MaxValue, Math.Ceiling(timeout.TotalSeconds)));
        }
    }
}
