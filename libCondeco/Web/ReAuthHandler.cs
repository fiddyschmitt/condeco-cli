using System.Net;
using System.Net.Http.Headers;

namespace libCondeco.Web
{
    //Detects HTTP 401 (Unauthorized) responses, transparently re-authenticates, and retries the
    //request once with the fresh credential. This sits at the top of the handler chain so callers
    //(e.g. the booking-confirmation loop) never have to know the session expired.
    //
    //Re-login is SINGLE-FLIGHT: at the booking rollover many requests run in parallel and all 401 at
    //once when the ~15-minute bearer expires. Only the first triggers a re-login; the rest wait for
    //it (via authVersion) and then retry with the new token. That avoids a re-login storm, which the
    //server throttles.
    //
    //Only idempotent GET requests are retried. A 401 on any method still triggers the re-login (so
    //the refreshed token is in place for later calls), but non-GET requests (e.g. the booking POST)
    //are not auto-retried: their body carries identifiers minted by the previous sign-in and
    //re-sending could mis-book.
    public class ReAuthHandler : DelegatingHandler
    {
        readonly SemaphoreSlim reAuthLock = new(1, 1);

        //Marks the requests issued by the re-login itself (login.aspx, the token exchange, the
        //post-login API calls) so they are never treated as a trigger for another re-login. AsyncLocal
        //flows through the re-login's call chain, so only that traffic is exempted - not other
        //in-flight requests on their own async flows.
        readonly AsyncLocal<bool> reAuthInProgress = new();

        //Bumped on each successful re-login. A request captures the version before sending; if another
        //request already re-logged in while it waited for the lock, the version differs and it skips
        //straight to retrying with the current token instead of re-logging in again.
        int authVersion;
        string? currentBearer;

        //Performs the full username/password sign-in and returns the new bearer token (or null if it
        //failed / no credentials are available). Set by the owning client after construction.
        public Func<string?>? ReLogin { get; set; }

        public ReAuthHandler(HttpMessageHandler innerHandler)
        {
            InnerHandler = innerHandler;
        }

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ReLogin == null || reAuthInProgress.Value)
            {
                return base.Send(request, cancellationToken);
            }

            var versionBeforeSend = Volatile.Read(ref authVersion);
            var response = base.Send(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            var bearer = ReAuthenticate(versionBeforeSend);
            if (bearer == null || request.Method != HttpMethod.Get)
            {
                return response;
            }

            response.Dispose();
            return base.Send(CloneWithBearer(request, bearer), cancellationToken);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (ReLogin == null || reAuthInProgress.Value)
            {
                return await base.SendAsync(request, cancellationToken);
            }

            var versionBeforeSend = Volatile.Read(ref authVersion);
            var response = await base.SendAsync(request, cancellationToken);

            if (response.StatusCode != HttpStatusCode.Unauthorized)
            {
                return response;
            }

            var bearer = ReAuthenticate(versionBeforeSend);
            if (bearer == null || request.Method != HttpMethod.Get)
            {
                return response;
            }

            response.Dispose();
            return await base.SendAsync(CloneWithBearer(request, bearer), cancellationToken);
        }

        //Single-flight re-login. Returns the current valid bearer (freshly minted here, or the one a
        //concurrent caller just minted), or null if re-login failed.
        string? ReAuthenticate(int versionBeforeSend)
        {
            reAuthLock.Wait();
            try
            {
                //Someone else already re-logged in while we waited for the lock - reuse their token.
                if (Volatile.Read(ref authVersion) != versionBeforeSend)
                {
                    return currentBearer;
                }

                reAuthInProgress.Value = true;
                string? bearer;
                try
                {
                    bearer = ReLogin!.Invoke();
                }
                finally
                {
                    reAuthInProgress.Value = false;
                }

                if (bearer != null)
                {
                    currentBearer = bearer;
                    Interlocked.Increment(ref authVersion);
                }
                return bearer;
            }
            finally
            {
                reAuthLock.Release();
            }
        }

        //A sent HttpRequestMessage cannot be re-sent, so clone it and apply the fresh bearer.
        static HttpRequestMessage CloneWithBearer(HttpRequestMessage request, string bearer)
        {
            var clone = new HttpRequestMessage(request.Method, request.RequestUri)
            {
                Version = request.Version,
                Content = request.Content
            };

            foreach (var header in request.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            clone.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
            return clone;
        }
    }
}
