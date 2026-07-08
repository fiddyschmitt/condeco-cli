using System;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace libCondeco.Web
{
    //Retries transient transport failures (e.g. a container DNS blip: EAI_AGAIN at the 00:01 autobook)
    //so a single hiccup doesn't kill the run. Only failures before the request is sent are retried.
    public class RetryHandler : DelegatingHandler
    {
        //Socket errors that can only occur while establishing the connection - safe to retry for any method.
        static readonly SocketError[] ConnectPhaseErrors =
        [
            SocketError.HostNotFound,       // EAI_NONAME
            SocketError.TryAgain,           // EAI_AGAIN
            SocketError.NoData,
            SocketError.ConnectionRefused,
            SocketError.TimedOut,
            SocketError.NetworkUnreachable,
            SocketError.HostUnreachable,
            SocketError.NetworkDown,
            SocketError.HostDown,
            SocketError.AddressNotAvailable,
        ];

        readonly int maxAttempts;
        readonly TimeSpan baseDelay;

        public RetryHandler(HttpMessageHandler innerHandler, int maxAttempts = 5, TimeSpan? baseDelay = null)
        {
            InnerHandler = innerHandler;
            this.maxAttempts = Math.Max(1, maxAttempts);
            this.baseDelay = baseDelay ?? TimeSpan.FromSeconds(1);
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return base.Send(request, cancellationToken);
                }
                catch (Exception ex) when (ShouldRetry(ex, request, attempt, cancellationToken))
                {
                    var delay = DelayFor(attempt);
                    Console.WriteLine($"{DateTime.Now}  Transient network error ({Describe(ex)}). Retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:N0}s...");
                    cancellationToken.WaitHandle.WaitOne(delay);
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await base.SendAsync(request, cancellationToken);
                }
                catch (Exception ex) when (ShouldRetry(ex, request, attempt, cancellationToken))
                {
                    var delay = DelayFor(attempt);
                    Console.WriteLine($"{DateTime.Now}  Transient network error ({Describe(ex)}). Retry {attempt}/{maxAttempts - 1} in {delay.TotalSeconds:N0}s...");
                    await Task.Delay(delay, cancellationToken);
                }
            }
        }

        bool ShouldRetry(Exception ex, HttpRequestMessage request, int attempt, CancellationToken cancellationToken)
        {
            if (attempt >= maxAttempts || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            var socketError = FindSocketError(ex);

            //POST/PUT: only retry connect-phase errors (nothing was sent) so a booking can't be duplicated.
            if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
            {
                return socketError.HasValue && Array.IndexOf(ConnectPhaseErrors, socketError.Value) >= 0;
            }

            //Idempotent methods: retry any transient transport failure.
            return ex is HttpRequestException || ex is TimeoutException || socketError.HasValue;
        }

        //Walk the inner-exception chain for a SocketException.
        static SocketError? FindSocketError(Exception? ex)
        {
            for (; ex != null; ex = ex.InnerException)
            {
                if (ex is SocketException se)
                {
                    return se.SocketErrorCode;
                }
            }
            return null;
        }

        //Jittered exponential backoff (1/2/4/8s...) capped at 10s.
        TimeSpan DelayFor(int attempt)
        {
            var seconds = Math.Min(baseDelay.TotalSeconds * Math.Pow(2, attempt - 1), 10);
            var jitterMs = Random.Shared.Next(0, 250);
            return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(jitterMs);
        }

        static string Describe(Exception ex)
        {
            var socketError = FindSocketError(ex);
            return socketError.HasValue ? socketError.Value.ToString() : ex.GetType().Name;
        }
    }
}
