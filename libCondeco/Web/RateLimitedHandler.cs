using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace libCondeco.Web
{
    public class RateLimitedHandler : DelegatingHandler
    {
        private readonly RateLimiter limiter;

        public RateLimitedHandler(RateLimiter limiter, HttpMessageHandler? innerHandler = null)
        {
            this.limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
            InnerHandler = innerHandler ?? new HttpClientHandler();
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _ = limiter.AcquireAsync(1, cancellationToken).AsTask().GetAwaiter().GetResult();

            var result = base.Send(request, cancellationToken);
            return result;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            using var lease = await limiter.AcquireAsync(1, cancellationToken);

            if (!lease.IsAcquired)
            {
                throw new InvalidOperationException("Rate limit exceeded and queue is full.");
            }

            return await base.SendAsync(request, cancellationToken);
        }
    }
}
