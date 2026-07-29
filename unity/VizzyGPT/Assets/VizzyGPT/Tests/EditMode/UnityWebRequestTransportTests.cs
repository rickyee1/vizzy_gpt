using System;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using VizzyGPT.Core.Api;
using VizzyGPT.Runtime.Api;

namespace VizzyGPT.Tests.EditMode
{
    public sealed class UnityWebRequestTransportTests
    {
        [Test]
        public void Send_async_honors_an_already_cancelled_request()
        {
            var request = new HttpTransportRequest(
                "POST",
                new Uri("https://example.invalid/v1/chat/completions"),
                new Dictionary<string, string> { { "Authorization", "Bearer test" } },
                new byte[] { 123, 125 },
                TimeSpan.FromSeconds(1));
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            Assert.Throws<OperationCanceledException>(() =>
                new UnityWebRequestTransport().SendAsync(request, cancellation.Token).GetAwaiter().GetResult());
        }
    }
}
