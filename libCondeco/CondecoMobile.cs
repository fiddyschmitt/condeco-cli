using libCondeco.Extensions;
using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.Mobile.Responses;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using libCondeco.Model.Web.Responses;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace libCondeco
{
    public class CondecoMobile : ICondeco
    {
        readonly HttpClientHandler clientHandler;
        readonly HttpClient client;

        string userIdLong = string.Empty;
        LoginInformationsV2Response? loginInfo;
        bool loginSuccessful = false;

        public string BaseUrl { get; }

        public CondecoMobile(string baseUrl)
        {
            clientHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer()
            };

            client = new HttpClient(clientHandler)
            {
                BaseAddress = new Uri(baseUrl),
            };
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
                            .Where(location => location.Name == locationName)
                            .SelectMany(location => location.WSTypes)
                            .Where(wsType => wsType.WSTypeName == workspaceTypeName)
                            .SelectMany(ws => ws.Groups)
                            .FirstOrDefault(grp => grp.Name == groupName) ?? throw new Exception($"Could not retrieve group. [{nameof(locationName)}: {locationName}] [{nameof(workspaceTypeName)}: {workspaceTypeName}] [{nameof(groupName)}: {locationName}]");

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
            var url = $"/MobileAPI/MobileService.svc/User/LoginInformationsV2?token={userIdLong}&currentDateTime={DateTime.Now:dd/MM/yyyy}&languageId=1&currentCulture=en-US";
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

        public static string Encrypt(
                string plaintext,
                string url,
                string version)
        {

            var c02 = T_c0(url.Replace("https", "", StringComparison.OrdinalIgnoreCase), true);
            var c03 = T_c0(version, false);

            var keyBytes = Encoding.UTF8.GetBytes(c02); // 32 bytes
            var ivBytes = Encoding.UTF8.GetBytes(c03); // 16 bytes

            var result = AesEncryptToBase64(Encoding.UTF8.GetBytes(plaintext), keyBytes, ivBytes);
            return result;
        }

        private static string T_c0(string value, bool thirtyTwo)
        {
            var lower = value.ToLowerInvariant();
            var b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(lower)); // NO_WRAP equivalent
            int targetLen = thirtyTwo ? 32 : 16;
            if (b64.Length < targetLen)
                b64 = b64 + new string('0', targetLen - b64.Length);
            if (b64.Length > targetLen)
                b64 = b64.Substring(0, targetLen);
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
            return Convert.ToBase64String(ct);
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

            var country = countries.FirstOrDefault(country => country.Name == countryName) ?? throw new Exception($"Could not find country: {countryName}");
            var location = country.Locations.FirstOrDefault(location => location.Name == locationName) ?? throw new Exception($"Could not find location: {locationName}");
            var group = location.Groups.FirstOrDefault(group => group.Name == groupName) ?? throw new Exception($"Could not find group: {groupName}");
            var floor = group.Floors.FirstOrDefault(floor => floor.Name == floorName) ?? throw new Exception($"Could not find floor: {floorName}");

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
            var geoInfo = GetGeoInformation();

            return geoInfo.CurrentTimeUTC;
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
            var bookingWindowSizeDays = groupSettings.advancePeriodValue;
            if (groupSettings.advancePeriodUnit == 1)
            {
                //I think this means that advancePeriodValue is in weeks
                bookingWindowSizeDays *= 7;
            }

            var startOfWeek = DateTime.Now.Date.StartOfWeek(DayOfWeek.Sunday);
            var result = startOfWeek.AddDays(bookingWindowSizeDays);
            return result;
        }

        public GroupSettingsWithRestrictions GetGroupSettings(int locationId, int groupId)
        {
            var getResponse = client.GetAsync($"/MobileAPI/DeskBookingService.svc/groupSettingsWithRestrictions?accessToken={userIdLong}&bookingForUserId=-1&locationId={locationId}&groupIds={groupId}").Result;
            getResponse.EnsureSuccessStatusCode();
            var getResponseeStr = getResponse.Content.ReadAsStringAsync().Result;

            var groupSettings = getResponseeStr.ToObject<GroupSettingsWithRestrictions>();
            return groupSettings;
        }

        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser)
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

            var url = $"/MobileAPI/DeskBookingService.svc/Book?accessToken={userIdLong}&userID={userIdToBookFor}&locationID={room.LocationId}&groupID={room.GroupId}&floorID={room.FloorId}&deskID={room.RoomId}&startDate={date:dd/MM/yyyy}|3";
            var responseStr = client.GetStringAsync(url).Result;

            var response = responseStr.ToObject<BookingResponse>();
            if (response == null)
            {
                return (false, new BookingResponse()
                {
                    CallResponse = new Callresponse()
                    {
                        ResponseCode = 0,
                        ResponseMessage = "Unsuccessful"
                    },
                    CreatedBookings = []
                });
            }
            else
            {
                if (response.CallResponse.ResponseCode == 100)
                {
                    return (true, response);
                }
                else
                {
                    return (false, response);
                }
            }
        }

        public List<UpcomingBooking> GetUpcomingBookings()
        {
            var ianaTimezoneStr = TimeZoneConverter.TZConvert.WindowsToIana(TimeZoneInfo.Local.Id);

            var url = $"/MobileAPI/MobileService.svc/MyBookings/ListV2?sessionGuid={userIdLong}&languageId=1&deskStartDate={DateTime.Now:dd/MM/yyyy}&deskEndDate={DateTime.Now.AddDays(7):dd/MM/yyyy}&roomStartDate={DateTime.Now:dd/MM/yyyy}&timeZoneID={ianaTimezoneStr}&pageSize=100&pageIndex=0";

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
            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;

            //use the response for /MobileAPI/MobileService.svc/User/LoginWithMagicLink
        }
    }
}


