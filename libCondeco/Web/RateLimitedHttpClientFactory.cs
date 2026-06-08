using System.Threading.RateLimiting;

namespace libCondeco.Web
{
    public class RateLimitedHttpClientFactory : IHttpClientFactory
    {
        private readonly FixedWindowRateLimiter limiter;
        private readonly bool verbose;

        public RateLimitedHttpClientFactory(bool verbose = false)
        {
            this.verbose = verbose;

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
            HttpMessageHandler innerHandler = verbose
                ? new LoggingHandler(clientHandler)
                : clientHandler;

            var handler = new RateLimitedHandler(limiter, innerHandler);

            var result = new HttpClient(handler);
            return result;
        }
    }
}
