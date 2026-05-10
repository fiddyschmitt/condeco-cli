using System.Threading.RateLimiting;

namespace libCondeco.Web
{
    public class RateLimitedHttpClientFactory : IHttpClientFactory
    {
        private readonly FixedWindowRateLimiter limiter;

        public RateLimitedHttpClientFactory()
        {
            limiter = new FixedWindowRateLimiter(new FixedWindowRateLimiterOptions
            {
                PermitLimit = 50,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst
            });
        }

        public HttpClient CreateClient(HttpMessageHandler clientHandler)
        {
            var handler = new RateLimitedHandler(limiter, clientHandler);

            var result = new HttpClient(handler);
            return result;
        }
    }
}
