namespace libCondeco.Web
{
    public class LoggingHandler : DelegatingHandler
    {
        public LoggingHandler(HttpMessageHandler innerHandler)
        {
            InnerHandler = innerHandler;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string? requestBody = null;
            if (request.Content != null)
            {
                requestBody = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var response = await base.SendAsync(request, cancellationToken);

            var body = "";
            if (response.Content != null)
            {
                await response.Content.LoadIntoBufferAsync();
                body = await response.Content.ReadAsStringAsync(cancellationToken);
            }

            var setCookies = response.Headers.TryGetValues("Set-Cookie", out var cookieValues)
                ? string.Join("; ", cookieValues)
                : "";

            var tagPrefix = "";
            if (request.Headers.TryGetValues("X-Booking-Tag", out var tagValues))
            {
                tagPrefix = $"[{tagValues.First()}]  ";
                request.Headers.Remove("X-Booking-Tag");
            }

            var logParts = new List<string>
            {
                $"{DateTime.Now}  {tagPrefix}HTTP {request.Method} {request.RequestUri?.PathAndQuery} -> {(int)response.StatusCode}"
            };

            if (!string.IsNullOrEmpty(requestBody))
            {
                logParts.Add($"  Request: {Truncate(requestBody, 2000)}");
            }

            if (!string.IsNullOrEmpty(setCookies))
            {
                logParts.Add($"  Set-Cookie: {setCookies}");
            }

            logParts.Add($"  Body: {Truncate(body, 2000)}");

            Console.WriteLine(string.Join(Environment.NewLine, logParts));

            return response;
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength) return value;
            return value[..maxLength] + $"... ({value.Length} chars total)";
        }
    }
}
