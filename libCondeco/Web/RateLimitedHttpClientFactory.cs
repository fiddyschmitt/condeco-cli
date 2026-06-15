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

        public HttpClient CreateClient(HttpMessageHandler clientHandler, Func<string?>? reLogin = null)
        {
            HttpMessageHandler innerHandler = verbose
                ? new LoggingHandler(clientHandler)
                : clientHandler;

            HttpMessageHandler handler = new RateLimitedHandler(limiter, innerHandler);

            //Outermost so its retry re-flows through rate-limiting + logging, and the re-login's own
            //traffic (issued via this same client) passes back through here and is exempted.
            if (reLogin != null)
            {
                handler = new ReAuthHandler(handler) { ReLogin = reLogin };
            }

            var result = new HttpClient(handler);
            return result;
        }
    }
}
