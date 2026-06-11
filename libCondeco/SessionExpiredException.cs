using System.Net;

namespace libCondeco
{
    public class SessionExpiredException : Exception
    {
        public SessionExpiredException(string message) : base(message)
        {
        }

        //Recognises session expiry however it surfaces: as this exception type, or as an
        //HttpRequestException with a 401 status (thrown by EnsureSuccessStatusCode/GetStringAsync),
        //possibly wrapped in AggregateException by .Result/.Wait()
        public static bool IsSessionExpired(Exception ex)
        {
            if (ex is SessionExpiredException)
            {
                return true;
            }

            if (ex is HttpRequestException httpRequestException && httpRequestException.StatusCode == HttpStatusCode.Unauthorized)
            {
                return true;
            }

            if (ex is AggregateException aggregateException)
            {
                return aggregateException.InnerExceptions.Any(IsSessionExpired);
            }

            if (ex.InnerException != null)
            {
                return IsSessionExpired(ex.InnerException);
            }

            return false;
        }
    }
}
