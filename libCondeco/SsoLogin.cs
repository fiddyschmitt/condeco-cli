using Newtonsoft.Json.Linq;
using System.Text;

namespace libCondeco
{
    public class SsoConfig
    {
        public required string SsoUrl { get; set; }
        public required string ClientId { get; set; }
        public Dictionary<string, string> Parameters { get; set; } = [];

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"  SsoUrl:   {SsoUrl}");
            sb.AppendLine($"  ClientId: {ClientId}");
            foreach (var p in Parameters)
                sb.AppendLine($"  {p.Key}: {p.Value}");
            return sb.ToString();
        }
    }

    public class SsoTokens
    {
        public required string AccessToken { get; set; }
        public string? RefreshToken { get; set; }
        public string? IdToken { get; set; }
    }

    public static class SsoLogin
    {
        public static SsoConfig? ParseFromSystemInfo(JObject systemInfo)
        {
            if (systemInfo["authInfo"] is not JObject authInfo)
            {
                Console.WriteLine("[SSO] systeminfo has no authInfo object.");
                return null;
            }

            var type = authInfo["type"]?.ToString();
            Console.WriteLine($"[SSO] systeminfo authInfo.type = \"{type}\"");

            if (!string.Equals(type, "sso", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SSO] Not an SSO server (type != \"sso\").");
                return null;
            }

            var clientId = authInfo["client_id"]?.ToString() ?? "";
            var ssoUrl = authInfo["ssoUrl"]?.ToString() ?? "";

            Console.WriteLine($"[SSO] client_id = \"{clientId}\"");
            Console.WriteLine($"[SSO] ssoUrl    = \"{ssoUrl}\"");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(ssoUrl))
            {
                Console.WriteLine("[SSO] Missing client_id or ssoUrl in systeminfo. Cannot proceed with SSO.");
                return null;
            }

            var parameters = new Dictionary<string, string>();
            foreach (var prop in authInfo.Properties())
            {
                if (prop.Name is "type" or "client_id" or "ssoUrl") continue;

                //The app stringifies every extra value (String.valueOf), so include non-string primitives (eg. numbers, booleans) too
                if (prop.Value.Type is JTokenType.Object or JTokenType.Array or JTokenType.Null) continue;

                parameters[prop.Name] = prop.Value.ToString();
                Console.WriteLine($"[SSO] Extra param: {prop.Name} = \"{prop.Value}\"");
            }

            Console.WriteLine("[SSO] SSO config parsed from systeminfo successfully.");
            return new SsoConfig { SsoUrl = ssoUrl, ClientId = clientId, Parameters = parameters };
        }

        public static SsoConfig? ParseFromGlobalSettings(string globalSettingsJson, Func<string, string> decrypt)
        {
            Console.WriteLine("[SSO] Attempting to parse SSO config from GetGlobalSettings (encrypted)...");
            var obj = JObject.Parse(globalSettingsJson);

            var connectionType = DecryptField(obj, "ConnectionType", decrypt);
            Console.WriteLine($"[SSO] Decrypted ConnectionType = \"{connectionType}\"");

            if (!string.Equals(connectionType, "sso", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("[SSO] ConnectionType is not \"sso\". Not an SSO server.");
                return null;
            }

            var ssoUrl = DecryptField(obj, "PingOAuthURL", decrypt) ?? "";
            var clientId = DecryptField(obj, "PingOAuthClientID", decrypt) ?? "";

            Console.WriteLine($"[SSO] Decrypted PingOAuthURL      = \"{ssoUrl}\"");
            Console.WriteLine($"[SSO] Decrypted PingOAuthClientID  = \"{clientId}\"");

            if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(ssoUrl))
            {
                Console.WriteLine("[SSO] Missing PingOAuthURL or PingOAuthClientID. Cannot proceed with SSO.");
                return null;
            }

            var parameters = new Dictionary<string, string>();

            var idp = DecryptField(obj, "PingOAuthIdpAdapterID", decrypt);
            if (!string.IsNullOrEmpty(idp)) { parameters["idp"] = idp; Console.WriteLine($"[SSO] Decrypted idp = \"{idp}\""); }

            var scope = DecryptField(obj, "PingOAuthScopes", decrypt);
            if (!string.IsNullOrEmpty(scope)) { parameters["scope"] = scope; Console.WriteLine($"[SSO] Decrypted scope = \"{scope}\""); }

            var pingAdapter = DecryptField(obj, "PingAdapterID", decrypt);
            if (!string.IsNullOrEmpty(pingAdapter)) { parameters["PingAdapterID"] = pingAdapter; Console.WriteLine($"[SSO] Decrypted PingAdapterID = \"{pingAdapter}\""); }

            var authMode = DecryptField(obj, "AuthenticationMode", decrypt);
            if (!string.IsNullOrEmpty(authMode)) { parameters["AuthenticationMode"] = authMode; Console.WriteLine($"[SSO] Decrypted AuthenticationMode = \"{authMode}\""); }

            Console.WriteLine("[SSO] SSO config parsed from GetGlobalSettings successfully.");
            return new SsoConfig { SsoUrl = ssoUrl, ClientId = clientId, Parameters = parameters };
        }

        static string? DecryptField(JObject obj, string fieldName, Func<string, string> decrypt)
        {
            var value = obj[fieldName]?.ToString();
            if (string.IsNullOrEmpty(value)) return null;
            try { return decrypt(value); }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSO] Failed to decrypt {fieldName}: {ex.Message}");
                return null;
            }
        }

        public static bool SupportsDeviceCodeFlow(HttpClient client, SsoConfig config)
        {
            try
            {
                var discoveryUrl = config.SsoUrl.TrimEnd('/') + "/.well-known/openid-configuration";
                Console.WriteLine($"[SSO] Fetching OIDC discovery: {discoveryUrl}");

                var response = client.GetAsync(discoveryUrl).Result;
                Console.WriteLine($"[SSO] Discovery response: {(int)response.StatusCode} {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine("[SSO] Discovery endpoint not available. Device code flow not supported.");
                    return false;
                }

                var responseStr = response.Content.ReadAsStringAsync().Result;
                var discovery = JObject.Parse(responseStr);

                if (discovery["grant_types_supported"] is not JArray grantTypes)
                {
                    Console.WriteLine("[SSO] No grant_types_supported in discovery. Device code flow not supported.");
                    return false;
                }

                Console.WriteLine($"[SSO] grant_types_supported: {grantTypes}");

                var supported = grantTypes.Any(g => g.ToString() == "urn:ietf:params:oauth:grant-type:device_code");
                Console.WriteLine($"[SSO] Device code flow supported: {supported}");
                return supported;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSO] Error checking device code flow support: {ex.Message}");
                return false;
            }
        }

        public static SsoTokens DeviceCodeLogin(HttpClient client, SsoConfig config, Action<string, string> displayCallback, CancellationToken cancellationToken = default)
        {
            var deviceEndpoint = config.SsoUrl.TrimEnd('/') + "/as/token.oauth2";
            Console.WriteLine($"[SSO] Starting device code flow.");
            Console.WriteLine($"[SSO] Token endpoint: {deviceEndpoint}");

            var requestParams = new List<KeyValuePair<string, string>>
            {
                new("client_id", config.ClientId),
                new("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
            };

            if (config.Parameters.TryGetValue("scope", out var scope))
            {
                requestParams.Add(new("scope", scope));
                Console.WriteLine($"[SSO] Requesting scope: {scope}");
            }

            Console.WriteLine($"[SSO] Requesting device code...");
            var requestContent = new FormUrlEncodedContent(requestParams);
            var response = client.PostAsync(deviceEndpoint, requestContent, cancellationToken).Result;
            var responseStr = response.Content.ReadAsStringAsync(cancellationToken).Result;

            Console.WriteLine($"[SSO] Device code response: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"[SSO] Device code response body: {responseStr}");

            response.EnsureSuccessStatusCode();

            var deviceResponse = JObject.Parse(responseStr);
            var deviceCode = deviceResponse["device_code"]?.ToString() ?? throw new Exception("No device_code in response");
            var userCode = deviceResponse["user_code"]?.ToString() ?? throw new Exception("No user_code in response");
            var verificationUri = deviceResponse["verification_uri"]?.ToString()
                                  ?? deviceResponse["verification_url"]?.ToString()
                                  ?? throw new Exception("No verification_uri in response");
            var interval = deviceResponse["interval"]?.Value<int>() ?? 5;
            var expiresIn = deviceResponse["expires_in"]?.Value<int>() ?? 300;

            Console.WriteLine($"[SSO] device_code:      {deviceCode[..Math.Min(20, deviceCode.Length)]}...");
            Console.WriteLine($"[SSO] user_code:        {userCode}");
            Console.WriteLine($"[SSO] verification_uri: {verificationUri}");
            Console.WriteLine($"[SSO] interval:         {interval}s");
            Console.WriteLine($"[SSO] expires_in:       {expiresIn}s");

            displayCallback(verificationUri, userCode);

            Console.WriteLine($"[SSO] Polling for authorization...");
            var pollCount = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                Thread.Sleep(interval * 1000);
                pollCount++;

                var pollParams = new FormUrlEncodedContent(
                [
                    new KeyValuePair<string, string>("client_id", config.ClientId),
                    new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:device_code"),
                    new KeyValuePair<string, string>("device_code", deviceCode),
                ]);

                var pollResponse = client.PostAsync(deviceEndpoint, pollParams, cancellationToken).Result;
                var pollStr = pollResponse.Content.ReadAsStringAsync(cancellationToken).Result;

                Console.WriteLine($"[SSO] Poll #{pollCount}: {(int)pollResponse.StatusCode} {pollResponse.StatusCode}");

                if (pollResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[SSO] Authorization complete. Token received.");
                    Console.WriteLine($"[SSO] Token response: {pollStr[..Math.Min(200, pollStr.Length)]}...");
                    return ParseTokenResponse(pollStr);
                }

                var pollObj = JObject.Parse(pollStr);
                var error = pollObj["error"]?.ToString();
                var errorDesc = pollObj["error_description"]?.ToString();
                Console.WriteLine($"[SSO] Poll #{pollCount}: error={error}, description={errorDesc}");

                if (error == "authorization_pending" || error == "slow_down")
                {
                    if (error == "slow_down")
                    {
                        interval += 5;
                        Console.WriteLine($"[SSO] Slowing down. New interval: {interval}s");
                    }
                    continue;
                }

                throw new Exception($"SSO device code flow failed: {pollStr}");
            }

            throw new OperationCanceledException();
        }

        public static string BuildAuthorizationUrl(SsoConfig config, string redirectUri)
        {
            var sb = new StringBuilder();
            sb.Append(config.SsoUrl.TrimEnd('/'));
            sb.Append("/as/authorization.oauth2");
            sb.Append("?response_type=code");
            sb.Append("&client_id=");
            sb.Append(Uri.EscapeDataString(config.ClientId));
            sb.Append("&redirect_uri=");
            sb.Append(Uri.EscapeDataString(redirectUri));
            sb.Append("&prompt=login");
            sb.Append("&ssoUrl=");
            sb.Append(Uri.EscapeDataString(config.SsoUrl));

            foreach (var param in config.Parameters)
            {
                sb.Append('&');
                sb.Append(Uri.EscapeDataString(param.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(param.Value));
            }

            var url = sb.ToString();
            Console.WriteLine($"[SSO] Built authorization URL: {url}");
            return url;
        }

        public static SsoTokens ExchangeCodeForTokens(HttpClient client, SsoConfig config, string code, string redirectUri)
        {
            var tokenEndpoint = config.SsoUrl.TrimEnd('/') + "/as/token.oauth2";
            Console.WriteLine($"[SSO] Exchanging authorization code for tokens...");
            Console.WriteLine($"[SSO] Token endpoint: {tokenEndpoint}");
            Console.WriteLine($"[SSO] Code: {code[..Math.Min(20, code.Length)]}...");
            Console.WriteLine($"[SSO] Redirect URI: {redirectUri}");

            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("client_id", config.ClientId),
                new KeyValuePair<string, string>("grant_type", "authorization_code"),
                new KeyValuePair<string, string>("code", code),
                new KeyValuePair<string, string>("redirect_uri", redirectUri),
            ]);

            var response = client.PostAsync(tokenEndpoint, content).Result;
            var responseStr = response.Content.ReadAsStringAsync().Result;

            Console.WriteLine($"[SSO] Token exchange response: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"[SSO] Token exchange body: {responseStr[..Math.Min(300, responseStr.Length)]}...");

            response.EnsureSuccessStatusCode();

            var tokens = ParseTokenResponse(responseStr);
            Console.WriteLine($"[SSO] Token exchange successful.");
            Console.WriteLine($"[SSO] access_token length:  {tokens.AccessToken.Length}");
            Console.WriteLine($"[SSO] refresh_token present: {tokens.RefreshToken != null}");
            Console.WriteLine($"[SSO] id_token present:      {tokens.IdToken != null}");
            return tokens;
        }

        public static SsoTokens RefreshAccessToken(HttpClient client, SsoConfig config, string refreshToken)
        {
            var tokenEndpoint = config.SsoUrl.TrimEnd('/') + "/as/token.oauth2";
            Console.WriteLine($"[SSO] Refreshing access token...");
            Console.WriteLine($"[SSO] Token endpoint: {tokenEndpoint}");

            var content = new FormUrlEncodedContent(
            [
                new KeyValuePair<string, string>("grant_type", "refresh_token"),
                new KeyValuePair<string, string>("refresh_token", refreshToken),
            ]);

            var response = client.PostAsync(tokenEndpoint, content).Result;
            var responseStr = response.Content.ReadAsStringAsync().Result;

            Console.WriteLine($"[SSO] Refresh response: {(int)response.StatusCode} {response.StatusCode}");
            Console.WriteLine($"[SSO] Refresh body: {responseStr[..Math.Min(300, responseStr.Length)]}...");

            response.EnsureSuccessStatusCode();

            var tokens = ParseTokenResponse(responseStr);
            Console.WriteLine($"[SSO] Refresh successful. New access_token length: {tokens.AccessToken.Length}");
            return tokens;
        }

        static SsoTokens ParseTokenResponse(string json)
        {
            var obj = JObject.Parse(json);
            return new SsoTokens
            {
                AccessToken = obj["access_token"]?.ToString() ?? throw new Exception($"No access_token in SSO token response: {json}"),
                RefreshToken = obj["refresh_token"]?.ToString(),
                IdToken = obj["id_token"]?.ToString(),
            };
        }

        public const string OobRedirectUri = "urn:ietf:wg:oauth:2.0:oob";
        public const string AppRedirectUri = "com.condecosoftware.condeco://oidc_callback";
    }
}
