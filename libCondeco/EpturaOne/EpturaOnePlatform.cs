using libCondeco.Extensions;
using libCondeco.Model.Web.Responses;
using System.Text;

namespace libCondeco.EpturaOne
{
    //Resolved Eptura One ("platform") configuration for a tenant, derived from /api/systeminfo.
    //The plaintext URLs come straight from systeminfo; BaseUrl/PlatformId/PingAuthHeader are the
    //encrypted fields decrypted with the same scheme as classic credentials (CondecoMobile.Decrypt).
    public class PlatformConfig
    {
        public required string PingUrl { get; init; }       // platformPingURL (plaintext)
        public required string AuthUrl { get; init; }        // platformAuthURL (plaintext, the OAuth host)
        public string? BaseUrl { get; init; }                // decrypted platformBaseURL (e.g. https://home.epturacloud.com/)
        public string? PlatformId { get; init; }             // decrypted platformId (a GUID)
        public string? PingAuthHeader { get; init; }         // decrypted+base64-decoded platformRef (server does not require it)

        //The email->tenant discovery API is platform-global (not per tenant). This is the host that
        //answers GET /api/v1/auth/app?emailId= unauthenticated.
        public const string DiscoveryHost = "https://auth.epturacloud.com";

        public static PlatformConfig? FromSystemInfo(SystemInfoResponse systemInfo, string host)
        {
            if (!systemInfo.isPlatform)
            {
                return null;
            }

            string? Decrypt(string? cipher)
            {
                if (string.IsNullOrEmpty(cipher))
                {
                    return null;
                }
                return CondecoMobile.Decrypt(cipher, host, systemInfo.appVersion);
            }

            var baseUrl = Decrypt(systemInfo.platformBaseURL);
            var platformId = Decrypt(systemInfo.platformId);

            //platformRef decrypts to a base64 string; the app Base64-decodes that to get the header value
            string? pingAuthHeader = null;
            var decryptedRef = Decrypt(systemInfo.platformRef);
            if (!string.IsNullOrEmpty(decryptedRef))
            {
                try
                {
                    pingAuthHeader = Encoding.UTF8.GetString(System.Convert.FromBase64String(decryptedRef));
                }
                catch
                {
                    pingAuthHeader = decryptedRef;
                }
            }

            return new PlatformConfig
            {
                PingUrl = systemInfo.platformPingURL ?? "",
                AuthUrl = systemInfo.platformAuthURL ?? "",
                BaseUrl = baseUrl,
                PlatformId = platformId,
                PingAuthHeader = pingAuthHeader
            };
        }
    }

    public static class PlatformDiscovery
    {
        //Email -> tenant(s) lookup. Unauthenticated GET against the platform discovery host.
        //Returns an empty list if the email is not recognised (the server returns 404 in that case).
        public static List<PlatformPingResponse> DiscoverByEmail(HttpClient client, string email, string? discoveryHost = null)
        {
            var host = (discoveryHost ?? PlatformConfig.DiscoveryHost).TrimEnd('/');
            var url = $"{host}/api/v1/auth/app?emailId={Uri.EscapeDataString(email)}";

            var response = client.GetAsync(url).Result;
            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var json = response.Content.ReadAsStringAsync().Result;
            return json.ToObject<List<PlatformPingResponse>>() ?? [];
        }
    }
}
