using libCondeco.EpturaOne;
using libCondeco.Extensions;
using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.Mobile.Responses;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using libCondeco.Model.Web.Responses;
using libCondeco.Web;
using Newtonsoft.Json.Linq;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace libCondeco
{
    public class CondecoMobile : ICondeco
    {
        readonly HttpMessageHandler clientHandler;
        readonly HttpClient client;

        string userIdLong = string.Empty;
        LoginInformationsV2Response? loginInfo;
        bool loginSuccessful = false;

        public string BaseUrl { get; }

        public CondecoMobile(IHttpClientFactory httpClientFactory, string baseUrl)
        {
            baseUrl = baseUrl.NormalizeBaseUrl();

            clientHandler = new SocketsHttpHandler
            {
                CookieContainer = new CookieContainer(),
                ConnectTimeout = TimeSpan.FromMinutes(15)
            };

            client = httpClientFactory.CreateClient(clientHandler);
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(15);

            BaseUrl = baseUrl;
        }

        public (bool Success, string ErrorMessage) LogIn(string token)
        {
            userIdLong = token;
            loginInfo = null;
            loginSuccessful = false;

            try
            {
                loginInfo = GetLoginInformation();
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            if (string.IsNullOrEmpty(loginInfo.UserEmail))
            {
                return (false, "Login unsuccessful");
            }
            else
            {
                userIdLong = token;
                loginSuccessful = true;

                return (true, "");
            }
        }

        public SsoConfig? DetectSso()
        {
            string? appVersion = null;
            var url = client.BaseAddress?.Host ?? "";

            try
            {
                var systemInfoResponse = client.GetAsync("/api/systeminfo").Result;
                if (systemInfoResponse.IsSuccessStatusCode)
                {
                    var json = systemInfoResponse.Content.ReadAsStringAsync().Result;
                    var obj = JObject.Parse(json);
                    appVersion = obj["appVersion"]?.ToString();

                    var ssoConfig = SsoLogin.ParseFromSystemInfo(obj);
                    if (ssoConfig != null) return ssoConfig;
                }
            }
            catch { }

            if (appVersion == null)
            {
                Console.WriteLine("[SSO] No appVersion from systeminfo — cannot decrypt GlobalSettings.");
                return null;
            }

            try
            {
                var globalSettingsResponse = client.GetAsync("/MobileAPI/DeskBookingService.svc/Configuration/GetGlobalSettings").Result;
                if (globalSettingsResponse.IsSuccessStatusCode)
                {
                    var json = globalSettingsResponse.Content.ReadAsStringAsync().Result;
                    var ver = appVersion;
                    return SsoLogin.ParseFromGlobalSettings(json, value => Decrypt(value, url, ver));
                }
            }
            catch { }

            return null;
        }

        //Detects whether this tenant is on the Eptura One ("platform") backend and, if so, returns
        //the resolved platform config (URLs + decrypted base/id/ref). Returns null for classic tenants.
        public PlatformConfig? DetectPlatform()
        {
            var host = client.BaseAddress?.Host ?? "";

            try
            {
                var systemInfoResponse = client.GetAsync("/api/systeminfo").Result;
                if (systemInfoResponse.IsSuccessStatusCode)
                {
                    var json = systemInfoResponse.Content.ReadAsStringAsync().Result;
                    var systemInfo = json.ToObject<SystemInfoResponse>();
                    return PlatformConfig.FromSystemInfo(systemInfo, host);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Platform] Detection failed: {ex.Message}");
            }

            return null;
        }

        //Eptura One platform-token exchange: trades an SSO/IdP access token for a platform
        //session token. Called on the tenant host.
        //GET /MobileAPI/mobileservice.svc/login/ValidatePlatformToken, Authorization = "Bearer " + ssoAccessToken.
        public JsonPlatFormToken? ValidatePlatformToken(string ssoAccessToken, string currentCulture = "en-US")
        {
            var url = $"/MobileAPI/mobileservice.svc/login/ValidatePlatformToken?currentCulture={Uri.EscapeDataString(currentCulture)}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + ssoAccessToken);

            var response = client.Send(request);
            Console.WriteLine($"[Platform] ValidatePlatformToken HTTP response: {(int)response.StatusCode} {response.StatusCode}");
            var json = response.Content.ReadAsStringAsync().Result;

            return json.ToObject<JsonPlatFormToken>();
        }

        //Logs in using an SSO/IdP access token. For Eptura One ("platform") tenants this first
        //exchanges the SSO token for a platform session token (ValidatePlatformToken), then logs in
        //with it; for classic tenants the SSO token is used directly.
        //Platform path: a "Bearer "-prefixed exchange on the tenant host, then the response's
        //sessionToken used as the session GUID (the "accessToken" query param, via LogIn) and its
        //token sent as the "Authorization: Bearer" header on every later call. Not yet confirmed
        //live against a real tenant.
        public (bool Success, string ErrorMessage) LogInWithSsoAccessToken(string ssoAccessToken)
        {
            var platform = DetectPlatform();
            if (platform != null)
            {
                Console.WriteLine("[Platform] Eptura One (platform) tenant. This login path is experimental and has not been confirmed against a real account - please report back whether it worked.");
                Console.WriteLine("[Platform] Exchanging the SSO token for a platform session token...");
                try
                {
                    var platformToken = ValidatePlatformToken(ssoAccessToken);
                    if (platformToken != null && platformToken.Success && !string.IsNullOrEmpty(platformToken.SessionToken))
                    {
                        //Match the app: sessionToken is the session GUID (LogIn puts it in the "accessToken"
                        //query param) and token is the "Authorization: Bearer" header on subsequent calls.
                        if (!string.IsNullOrEmpty(platformToken.Token))
                        {
                            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", platformToken.Token);
                        }
                        return LogIn(platformToken.SessionToken);
                    }
                    Console.WriteLine("[Platform] ValidatePlatformToken did not return a session token; falling back to the raw SSO token.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Platform] ValidatePlatformToken failed: {ex.Message}. Falling back to the raw SSO token.");
                }
            }

            return LogIn(ssoAccessToken);
        }

        public (bool Success, string ErrorMessage, SsoTokens? Tokens) SsoLogIn(
            SsoConfig ssoConfig,
            string? existingRefreshToken,
            Func<string>? promptForAuthCode,
            Action<string>? display = null,
            CancellationToken cancellationToken = default)
        {
            var print = display ?? Console.WriteLine;

            Console.WriteLine("[SSO] === Starting SSO Login ===");
            Console.WriteLine($"[SSO] Config:\n{ssoConfig}");

            var placeholder = SsoLogin.FindUnconfiguredPlaceholder(ssoConfig);
            if (placeholder != null)
            {
                var message = $"This server's SSO is not fully configured (it returned the placeholder \"{placeholder}\"). Contact your Condeco administrator.";
                Console.WriteLine($"[SSO] {message}");
                return (false, message, null);
            }

            if (!string.IsNullOrEmpty(existingRefreshToken))
            {
                Console.WriteLine("[SSO] Attempting refresh token login...");
                try
                {
                    var refreshedTokens = SsoLogin.RefreshAccessToken(client, ssoConfig, existingRefreshToken);
                    Console.WriteLine("[SSO] Refresh token succeeded. Logging in with access token...");
                    var (success, error) = LogInWithSsoAccessToken(refreshedTokens.AccessToken);
                    if (success)
                    {
                        Console.WriteLine("[SSO] Login with refreshed token successful.");
                        return (true, "", refreshedTokens);
                    }
                    Console.WriteLine($"[SSO] Login with refreshed token failed: {error}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SSO] Refresh token failed: {ex.Message}");
                }
            }

            if (SsoLogin.SupportsDeviceCodeFlow(client, ssoConfig))
            {
                Console.WriteLine("[SSO] Device code flow is supported. Starting...");
                try
                {
                    var tokens = SsoLogin.DeviceCodeLogin(client, ssoConfig, (verificationUri, userCode) =>
                    {
                        print("");
                        print("To sign in, open the following URL in a browser:");
                        print($"  {verificationUri}");
                        print("");
                        print($"And enter this code: {userCode}");
                        print("");
                        print("Waiting for authorization...");
                    }, cancellationToken);

                    Console.WriteLine("[SSO] Device code flow succeeded. Logging in with access token...");
                    var (success, error) = LogInWithSsoAccessToken(tokens.AccessToken);
                    if (success)
                    {
                        Console.WriteLine("[SSO] Login with device code token successful.");
                        return (true, "", tokens);
                    }
                    Console.WriteLine($"[SSO] Login with device code token failed: {error}");
                    return (false, $"SSO token obtained but login failed: {error}", tokens);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[SSO] Device code flow cancelled.");
                    return (false, "SSO login cancelled.", null);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[SSO] Device code flow failed: {ex.Message}");
                    Console.WriteLine("[SSO] Falling through to manual paste flow...");
                }
            }
            else
            {
                Console.WriteLine("[SSO] Device code flow not supported.");
            }

            Console.WriteLine("[SSO] Starting manual paste flow...");
            //Use the app's registered redirect URI. The IdP only accepts redirect URIs registered for the
            //client; the out-of-band URN is not one of them (it 400s with "Invalid redirect_uri"), whereas
            //the app's custom scheme is. The token exchange below reuses this same value (OAuth requires it).
            var redirectUri = SsoLogin.AppRedirectUri;
            var authUrl = SsoLogin.BuildAuthorizationUrl(ssoConfig, redirectUri);

            print("");
            print("Open this URL in a browser to sign in:");
            print($"  {authUrl}");
            print("");
            print("After signing in, your browser will try to open a 'com.condecosoftware.condeco://...' link");
            print("and show an error - that is expected on desktop. Copy that whole URL from the address bar");
            print("(or just the 'code' value from it) and paste it below.");
            print("");

            if (promptForAuthCode == null)
            {
                Console.WriteLine("[SSO] No auth code prompt callback provided. Cannot proceed with manual paste flow.");
                return (false, "SSO requires interactive input but no prompt callback was provided.", null);
            }

            var pastedValue = promptForAuthCode();
            var code = SsoLogin.ExtractAuthCode(pastedValue);
            if (string.IsNullOrEmpty(code))
            {
                Console.WriteLine("[SSO] No authorization code provided.");
                return (false, "No authorization code provided.", null);
            }

            Console.WriteLine($"[SSO] Authorization code received: {code[..Math.Min(20, code.Length)]}...");

            try
            {
                var tokens = SsoLogin.ExchangeCodeForTokens(client, ssoConfig, code, redirectUri);
                Console.WriteLine("[SSO] Code exchange succeeded. Logging in with access token...");
                var (success, error) = LogIn(tokens.AccessToken);
                if (success)
                {
                    Console.WriteLine("[SSO] Login with exchanged token successful.");
                    return (true, "", tokens);
                }
                Console.WriteLine($"[SSO] Login with exchanged token failed: {error}");
                return (false, $"SSO token obtained but login failed: {error}", tokens);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSO] Code exchange failed: {ex.Message}");
                return (false, $"SSO code exchange failed: {ex.Message}", null);
            }
        }

        public (bool Success, string ErrorMessage, SsoTokens? Tokens) RefreshSsoToken(SsoConfig ssoConfig, string refreshToken)
        {
            Console.WriteLine("[SSO] Attempting refresh token...");
            try
            {
                var refreshedTokens = SsoLogin.RefreshAccessToken(client, ssoConfig, refreshToken);
                Console.WriteLine("[SSO] Refresh succeeded. Logging in with new access token...");
                var (success, error) = LogInWithSsoAccessToken(refreshedTokens.AccessToken);
                if (success)
                {
                    Console.WriteLine("[SSO] Login with refreshed token successful.");
                    return (true, "", refreshedTokens);
                }
                Console.WriteLine($"[SSO] Login with refreshed token failed: {error}");
                return (false, error, refreshedTokens);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SSO] Refresh failed: {ex.Message}");
                return (false, $"Refresh failed: {ex.Message}", null);
            }
        }

        public (bool Success, string ErrorMessage) LogIn(string username, string password)
        {
            userIdLong = string.Empty;
            loginInfo = null;
            loginSuccessful = false;

            var systemInfoResponse = client.GetAsync("/api/systeminfo").Result;
            systemInfoResponse.EnsureSuccessStatusCode();

            var systemInfoJson = systemInfoResponse.Content.ReadAsStringAsync().Result;
            var systemInfo = systemInfoJson.ToObject<SystemInfoResponse>();

            var url = client.BaseAddress?.Host ?? "";

            var encryptedPassword = Encrypt(password, url, systemInfo.appVersion);

            var post = new
            {
                UserName = username,
                Password = encryptedPassword,
                ConnectionType = 1,
                CallingFrom = 3,
                IsFromMobile = 1
            };
            var postStr = post.ToJson();

            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync($"/LoginAPI/auth/authenticateusersecure", postContent).Result;
            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;

            var authResponse = postResponseStr.ToObject<AuthenticateUserSecureResponse>();

            if (authResponse.Result.LoginResult == 0)
            {
                return (false, "Account is currently suspended.");
            }

            if (authResponse.Result.LoginResult == 2)
            {
                userIdLong = authResponse.Result.Token;

                try
                {
                    loginInfo = GetLoginInformation();
                }
                catch (Exception ex)
                {
                    return (false, ex.Message);
                }

                loginSuccessful = true;

                return (true, "");
            }
            else
            {
                return (false, "Login invalid.");
            }
        }

        public Model.Mobile.Responses.Group GetGroup(string locationName, string workspaceTypeName, string groupName)
        {
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            var result = loginInfo
                            .DeskResults
                            .MasterData
                            .SelectMany(md => md.LocationsV2)
                            .Where(location => location.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(location => location.WSTypes)
                            .Where(wsType => wsType.WSTypeName.Equals(workspaceTypeName, StringComparison.OrdinalIgnoreCase))
                            .SelectMany(ws => ws.Groups)
                            .FirstOrDefault(grp => grp.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Could not retrieve group. [{nameof(locationName)}: {locationName}] [{nameof(workspaceTypeName)}: {workspaceTypeName}] [{nameof(groupName)}: {groupName}]");

            return result;
        }

        public bool CanBookForOthers(string locationName, string workspaceTypeName, string groupName)
        {
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            var groupSettings = GetGroup(locationName, workspaceTypeName, groupName);

            /*
            var EnumAllowDesksToBeBookedForUsers = {
                AllUsers: 1,
                OnlyAdmins: 2,
                OnlyAdminsAndLocSCEnabled: 3
            };

            //todo: Determine if canBookForOthers can be false, even though the site-wide setting (bookingPermissions.DeskResults.SystemSettings.CanBookForOthersGlobal) is 1.
            */
            var canBookForOthers = groupSettings?.DeskSettings.CanBookForOthers ?? false;
            return canBookForOthers;
        }

        public bool CanBookForOthersExternal(string locationName, string workspaceTypeName, string groupName)
        {
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            var groupSettings = GetGroup(locationName, workspaceTypeName, groupName);

            var canBookForExternalUser = groupSettings?.DeskSettings.CanBookForExternalUser ?? false;
            return canBookForExternalUser;
        }

        public LoginInformationsV2Response GetLoginInformation()
        {
            var url = $"/MobileAPI/MobileService.svc/User/LoginInformationsV2?token={userIdLong}&currentDateTime={DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}&languageId=1&currentCulture=en-US";
            var responseStr = client.GetStringAsync(url).Result;

            var result = LoginInformationsV2Response.FromServerResponse(responseStr);
            return result;
        }

        public void LogOut()
        {
            //HTTP DELETE
            ///MobileAPI/MobileService.svc/notification/unregister?token=&installationId=
            ///MobileAPI/MobileService.svc/notification/unregister?token=&installationId=
        }

        private static string StripHttpsPrefix(string url)
        {
            if (url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return url["https://".Length..];
            return url;
        }

        public static string Encrypt(
                string plaintext,
                string url,
                string version)
        {
            var c02 = T_c0(StripHttpsPrefix(url), true);
            var c03 = T_c0(version, false);

            var keyBytes = Encoding.UTF8.GetBytes(c02);
            var ivBytes = Encoding.UTF8.GetBytes(c03);

            var result = AesEncryptToBase64(Encoding.UTF8.GetBytes(plaintext), keyBytes, ivBytes);
            return result;
        }

        private static string T_c0(string value, bool thirtyTwo)
        {
            var lower = value.ToLowerInvariant();
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(lower)); // NO_WRAP equivalent
            int targetLen = thirtyTwo ? 32 : 16;
            if (b64.Length < targetLen)
                b64 += new string('0', targetLen - b64.Length);
            if (b64.Length > targetLen)
                b64 = b64[..targetLen];
            return b64;
        }

        private static string AesEncryptToBase64(byte[] plaintext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;
            using var enc = aes.CreateEncryptor();
            var ct = enc.TransformFinalBlock(plaintext, 0, plaintext.Length);
            return Convert.ToBase64String(ct) + "\n";
        }

        public static string Decrypt(
        string ciphertextBase64,
        string url,
        string version)
        {
            var c02 = T_c0(StripHttpsPrefix(url), true);
            var c03 = T_c0(version, false);

            var keyBytes = Encoding.UTF8.GetBytes(c02); // 32 bytes
            var ivBytes = Encoding.UTF8.GetBytes(c03); // 16 bytes

            var plaintextBytes = AesDecryptFromBase64(ciphertextBase64, keyBytes, ivBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }

        private static byte[] AesDecryptFromBase64(string base64Ciphertext, byte[] key, byte[] iv)
        {
            using var aes = Aes.Create();
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = key;
            aes.IV = iv;

            using var dec = aes.CreateDecryptor();
            var ciphertextBytes = Convert.FromBase64String(base64Ciphertext);
            return dec.TransformFinalBlock(ciphertextBytes, 0, ciphertextBytes.Length);
        }

        public static string LegacyTwoStepDecrypt(string base64Ciphertext)
        {
            var packageName = "com.condecosoftware.condeco";
            var stage1Key = Encoding.UTF8.GetBytes((packageName + "00000")[..32]);
            var stage1Iv = Encoding.UTF8.GetBytes(packageName[..16]);

            var dynamicKey = AesDecryptFromBase64("Rv4nnT9Ni00prLokbhT/3m6PP6/kA1bjsoK4p9I8P2BH5BG/V+qDNodaRJhw4EbM", stage1Key, stage1Iv);
            var dynamicIv = AesDecryptFromBase64("j2ElkxFAeqQzY9lJOhuWlCVncatpeS9EeMffvEg/Pik=", stage1Key, stage1Iv);

            return Encoding.UTF8.GetString(AesDecryptFromBase64(
                base64Ciphertext,
                dynamicKey,
                dynamicIv));
        }

        public string GetFullName()
        {
            loginInfo ??= GetLoginInformation();

            var result = $"{loginInfo.DeskResults.UserFirstName} {loginInfo.DeskResults.UserLastName}";
            return result;
        }

        public string GetUserId()
        {
            loginInfo ??= GetLoginInformation();

            var result = $"{loginInfo.UserID}";
            return result;
        }

        public List<Model.People.Colleague> FindColleague(string searchTerm)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var url = $"/MobileAPI/DeskBookingService.svc/FindColleague?accessToken={userIdLong}&name={searchTerm}";
            var responseStr = client.GetStringAsync(url).Result;

            var findColleagueResponse = responseStr.ToObject<FindColleagueResponse>();

            var result = findColleagueResponse
                            .userDetails
                            .Select(user => new Model.People.Colleague()
                            {
                                UserId = $"{user.UserID}",
                                FullName = user.FullName,
                                Email = user.Email
                            })
                            .ToList();

            return result;
        }

        public List<Country> GetCountries()
        {
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            var result = loginInfo
                            .DeskResults
                            .MasterData
                            .Select(countryRec =>
                            {
                                var country = new Country
                                {
                                    Id = countryRec.ID,
                                    Name = countryRec.Name,
                                    Locations = countryRec
                                                    .LocationsV2
                                                    .Select(locationRec =>
                                                    {
                                                        var location = new Location()
                                                        {
                                                            Id = locationRec.ID,
                                                            Name = locationRec.Name
                                                        };

                                                        var groupToWorkspaceTypes = locationRec
                                                                                        .WSTypes
                                                                                        .SelectMany(wsType => wsType
                                                                                                                .Groups
                                                                                                                .Select(grp => new
                                                                                                                {
                                                                                                                    Group = grp,
                                                                                                                    WsType = new WorkspaceType()
                                                                                                                    {
                                                                                                                        Id = wsType.WSTypeId,
                                                                                                                        Name = wsType.WSTypeName
                                                                                                                    }
                                                                                                                })
                                                                                                                .ToList())
                                                                                        .GroupBy(
                                                                                            grp => grp.Group.ID,
                                                                                            grp => grp.WsType,
                                                                                            (k, workspaceTypes) => new
                                                                                            {
                                                                                                Group = k,
                                                                                                WsTypes = workspaceTypes.ToList()
                                                                                            })
                                                                                        .ToDictionary(grp => grp.Group);


                                                        location.Groups = locationRec
                                                                            .WSTypes
                                                                            .SelectMany(wsType => wsType.Groups)
                                                                            .Select(grp => new Model.Space.Group()
                                                                            {
                                                                                Id = grp.ID,
                                                                                Name = grp.Name,
                                                                                Floors = grp.Floors
                                                                                            .Select(floor => new Model.Space.Floor()
                                                                                            {
                                                                                                Id = floor.ID,
                                                                                                Name = floor.Name,
                                                                                                WorkspaceTypes = groupToWorkspaceTypes[grp.ID].WsTypes
                                                                                            })
                                                                                            .ToList()
                                                                            })
                                                                            .ToList();

                                                        return location;
                                                    })
                                                    .ToList()
                                };

                                return country;
                            })
                            .ToList();

            return result;
        }

        public FloorPlanResponse GetFloorPlan(int locationId, int groupId, int floorId)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var url = $"/MobileAPI/DeskBookingService.svc/floors/Floorplan?accessToken={userIdLong}&locationId={locationId}&groupId={groupId}&floorId={floorId}&IsV2=true";
            var responseStr = client.GetStringAsync(url).Result;

            var result = responseStr.ToObject<FloorPlanResponse>();
            return result;
        }

        public List<Room> GetRooms(string countryName, string locationName, string groupName, string floorName, string workstationTypeName)
        {
            var countries = GetCountries();

            var country = countries.FirstOrDefault(country => country.Name.Equals(countryName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Could not find country: {countryName}");
            var location = country.Locations.FirstOrDefault(location => location.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Could not find location: {locationName}");
            var group = location.Groups.FirstOrDefault(group => group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Could not find group: {groupName}");
            var floor = group.Floors.FirstOrDefault(floor => floor.Name.Equals(floorName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Could not find floor: {floorName}");

            var floorPlan = GetFloorPlan(location.Id, group.Id, floor.Id);

            var result = floorPlan
                            .FloorPlan
                            .ResourceCoordinates
                            .Select(res => new Room()
                            {
                                CountryId = country.Id,
                                LocationId = location.Id,
                                GroupId = group.Id,
                                FloorId = floor.Id,
                                RoomId = res.ResourceItemId,
                                Name = res.ResourceItemName,
                                WSTypeId = res.WSTypeID
                            })
                            .ToList();

            return result;
        }

        public GeoInformationResponse GetGeoInformation()
        {
            var post = new
            {
                UserID = $"{GetUserId()}",
                uniqLongUserIdueKey = $"{userIdLong}",
            };

            var postStr = post.ToJson();
            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync($"/webapi/GridDateSelection/ReturnGeoInformation", postContent).Result;
            postResponse.EnsureSuccessStatusCode();
            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;

            var geoInfo = postResponseStr.ToObject<GeoInformationResponse>();
            return geoInfo;
        }

        public DateTime GetServerDateTimeUTC()
        {
            var response = client.GetAsync("/api/systeminfo").Result;

            DateTime result;

            if (response.Headers.Date.HasValue)
            {
                result = response.Headers.Date.Value.UtcDateTime;
            }
            else
            {
                throw new Exception($"Server HTTP response did not contain a DateTime");
            }

            return result;
        }

        public DayOfWeek GetRolloverDay()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            // WeekStart is ISO-8601: 1=Monday .. 7=Sunday — the first day of the new booking week.
            // Rollover happens the evening before, so subtract one day.
            var weekStart = loginInfo.DeskResults.SystemSettings.WeekStart;
            return (DayOfWeek)((weekStart % 7 + 6) % 7);
        }

        public DateTime GetBookingWindowStartDate()
        {
            return DateTime.Now.Date;
        }

        public DateTime GetBookingWindowEndDate()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            var groupSettings = GetGroupSettings(loginInfo.DeskResults.DefaultSettings.DefaultLocation, loginInfo.DeskResults.DefaultSettings.DefaultGroup);

            // WeekStart is the first day of the booking week (ISO-8601: 1=Mon .. 7=Sun)
            var weekStart = (DayOfWeek)(loginInfo.DeskResults.SystemSettings.WeekStart % 7);

            var periodDays = groupSettings.advancePeriodValue;
            if (groupSettings.advancePeriodUnit == 1)
                periodDays *= 7;

            var weekBoundary = DateTime.Now.Date.StartOfWeek(weekStart);
            var result = weekBoundary.AddDays(periodDays);

            if (groupSettings.includeWeekend)
                result = result.AddDays(-1);
            else
                result = result.AddDays(-3);

            return result;
        }

        public (DateTime StartDate, DateTime EndDate) GetBookingWindow()
        {
            var startDate = GetBookingWindowStartDate();
            var endDate = GetBookingWindowEndDate();
            return (startDate, endDate);
        }

        public GroupSettingsWithRestrictions GetGroupSettings(int locationId, int groupId)
        {
            var getResponse = client.GetAsync($"/MobileAPI/DeskBookingService.svc/groupSettingsWithRestrictions?accessToken={userIdLong}&bookingForUserId=-1&locationId={locationId}&groupIds={groupId}").Result;
            getResponse.EnsureSuccessStatusCode();
            var getResponseeStr = getResponse.Content.ReadAsStringAsync().Result;

            var groupSettings = getResponseeStr.ToObject<GroupSettingsWithRestrictions>();
            return groupSettings;
        }

        public Task<HttpResponseMessage> SendBookingRequest(Room room, DateOnly date, BookFor? bookForUser)
        {
            var result = SendBookingRequest(room, [date], bookForUser);
            return result;
        }

        public Task<HttpResponseMessage> SendBookingRequest(Room room, List<DateOnly> dates, BookFor? bookForUser, string? tag = null)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");
            if (loginInfo == null) throw new Exception($"{nameof(loginInfo)} not yet retrieved.");

            if (bookForUser != null && bookForUser.IsExternal == "1")
            {
                throw new Exception($"Booking for an external user using the Mobile API is not yet supported.");
            }

            var userIdToBookFor = -1;   //current user
            if (bookForUser != null && !string.IsNullOrEmpty(bookForUser.UserId))
            {
                userIdToBookFor = int.Parse(bookForUser.UserId);
            }

            var dateStr = dates
                            .Select(date => $"{date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}|3")
                            .ToString(",");

            var url = $"/MobileAPI/DeskBookingService.svc/Book?accessToken={userIdLong}&userID={userIdToBookFor}&locationID={room.LocationId}&groupID={room.GroupId}&floorID={room.FloorId}&deskID={room.RoomId}&startDate={dateStr}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (tag != null)
            {
                request.Headers.Add("X-Booking-Tag", tag);
            }

            var result = client.SendAsync(request);
            return result;
        }

        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser)
        {
            var httpRequest = SendBookingRequest(room, date, bookForUser);

            string? bookingResponseStr;
            try
            {
                bookingResponseStr = httpRequest.Result.Content.ReadAsStringAsync().Result;
            }
            catch (Exception ex)
            {
                //Catch things such as timeouts
                bookingResponseStr = $"Error while booking: {ex.Message}";
            }

            var successful = false;
            try
            {
                successful = BookingSuccessful(room, date, bookForUser);
            }
            catch (Exception ex)
            {
                //Catch things such as timeouts
                bookingResponseStr ??= $"Error while confirming booking: {ex.Message}";
            }

            if (successful)
            {
                try
                {
                    //Sometimes this contains "You have already reserved this workspace type for this time slot", instead of BookingResponse object
                    if (!string.IsNullOrEmpty(bookingResponseStr))
                    {
                        var bookingResponseObj = bookingResponseStr.ToObject<BookingResponse>();
                        return (true, bookingResponseObj);
                    }
                }
                catch
                {

                }

                var condecoBookingResponse = new BookingResponse()
                {
                    CallResponse = new Callresponse()
                    {
                        ResponseCode = 100,
                        ResponseMessage = $"Booking confirmed"
                    },
                    CreatedBookings = []
                };

                return (true, condecoBookingResponse);
            }
            else
            {
                var condecoBookingResponse = new BookingResponse()
                {
                    CallResponse = new Callresponse()
                    {
                        ResponseCode = 0,
                        ResponseMessage = $"{bookingResponseStr}"
                    },
                    CreatedBookings = []
                };

                return (false, condecoBookingResponse);
            }
        }

        public List<UpcomingBooking> GetUpcomingBookings(DateOnly? fromDate = null, DateOnly? toDate = null)
        {
            var ianaTimezoneStr = TimeZoneConverter.TZConvert.WindowsToIana(TimeZoneInfo.Local.Id);

            fromDate ??= DateOnly.FromDateTime(DateTime.Now.Date);
            toDate ??= fromDate;

            var deskStartDateStr = fromDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var deskEndDateStr = toDate.Value.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
            var roomStartDateStr = DateTime.Now.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

            var url = $"/MobileAPI/MobileService.svc/MyBookings/ListV2?sessionGuid={userIdLong}&languageId=1&deskStartDate={deskStartDateStr}&deskEndDate={deskEndDateStr}&roomStartDate={roomStartDateStr}&timeZoneID={ianaTimezoneStr}&pageSize=100&pageIndex=0";

            var listResultsStr = client.GetStringAsync(url).Result;

            var listResults = listResultsStr.ToObject<ListV2Response>();

            var result = listResults
                            .DeskBookings
                            .Select(deskBooking => new UpcomingBooking()
                            {
                                BookingId = deskBooking.BookingID,
                                BookingItemId = 0,
                                LocationId = deskBooking.LocationID,
                                BookedLocation = deskBooking.LocationName,
                                DeskId = deskBooking.DeskID,
                                BookingTitle = deskBooking.DeskName,
                                BookingStartDate = DateTime.Parse(deskBooking.BookingStart),
                                BookingEndDate = DateTime.Parse(deskBooking.BookingEnd),
                                BookingStatus = deskBooking.Status,
                                CheckInRequired = true,

                                BookedForSelf = deskBooking.AdditionalInformation == null,
                                BookedForUserId = deskBooking.AdditionalInformation?.UserID,
                                BookedForFullName = deskBooking.AdditionalInformation?.FullName,
                            })
                            .ToList();

            return result;
        }

        public bool BookingSuccessful(Room room, DateOnly bookedForDate, BookFor? bookingFor)
        {
            var bookings = GetUpcomingBookings(bookedForDate, bookedForDate);
            var userId = GetUserId();

            var successful = bookings
                                .Where(booking =>
                                {
                                    var startDate = DateOnly.FromDateTime(booking.BookingStartDate);
                                    var endDateTime = DateOnly.FromDateTime(booking.BookingEndDate);

                                    var matchesDate = bookedForDate >= startDate && bookedForDate <= endDateTime;
                                    return matchesDate;

                                })
                                .Where(booking => booking.DeskId == room.RoomId)
                                .Where(booking =>
                                {
                                    bool matchesUser;

                                    if (bookingFor?.IsExternal == "1")
                                    {
                                        matchesUser = booking.BookedForFullName == $"{bookingFor.FirstName} {bookingFor.LastName}";
                                    }
                                    else
                                    {
                                        if (string.IsNullOrEmpty(bookingFor?.UserId))
                                        {
                                            matchesUser = true;
                                        }
                                        else
                                        {
                                            var userIdMatch = "" + booking.BookedForUserId == bookingFor.UserId;
                                            var fullNameMatch = booking.BookedForFullName == $"{bookingFor.FirstName} {bookingFor.LastName}";

                                            matchesUser = userIdMatch || fullNameMatch;
                                        }
                                    }

                                    return matchesUser;
                                })
                                .Any();

            return successful;
        }

        public (bool Success, string BookingStatusStr) CheckIn(UpcomingBooking bookingDetails)
        {
            var url = $"/MobileAPI/DeskBookingService.svc/CheckIn?accessToken={userIdLong}&locationID={bookingDetails.LocationId}&deskID={bookingDetails.DeskId}&qrCode=";
            var responseStr = client.GetStringAsync(url).Result;

            var response = responseStr.ToObject<CheckInResponse>();
            var success = response.CallResponse.ResponseCode == 100;

            var result = (success, response.CallResponse.ResponseMessage);
            return result;
        }

        //FPS 08/08/2025: This does not seem to be available, because it currently returns 404.
        public void SendMagicLink(string email, string uniqueKey)
        {
            var post = new
            {
                email = $"{email}",
                uniqueKey = $"{uniqueKey}",
            };

            var postStr = post.ToJson();
            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync($"/MobileAPI/MobileService.svc/User/SendMagicLink", postContent).Result;
            postResponse.EnsureSuccessStatusCode();
            _ = postResponse.Content.ReadAsStringAsync().Result;

            //use the response for /MobileAPI/MobileService.svc/User/LoginWithMagicLink
        }
    }
}


