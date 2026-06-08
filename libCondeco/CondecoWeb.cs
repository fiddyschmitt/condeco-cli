using libCondeco.Extensions;
using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using libCondeco.Model.Web;
using libCondeco.Model.Web.Responses;
using libCondeco.Web;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.RateLimiting;
using System.Web;

namespace libCondeco
{
    public class CondecoWeb : ICondeco
    {
        readonly HttpClientHandler clientHandler;
        readonly HttpClient client;
        bool loginSuccessful = false;

        //GetGridSettings returns 403 if called too frequently. This limiter is to prevent that.
        readonly FixedWindowRateLimiter gridSettingsLimiter = new(new FixedWindowRateLimiterOptions
        {
            PermitLimit = 1,
            Window = TimeSpan.FromSeconds(1),
            QueueLimit = int.MaxValue,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        });

        public string BaseUrl { get; }

        //Session-related info
        string? userId;
        string? userIdLong;
        string userFullName = string.Empty;
        AppSettingResponse? AppSettings;     //app settings as provided by web server

        public CondecoWeb(IHttpClientFactory httpClientFactory, string baseUrl)
        {
            clientHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer()
            };

            client = httpClientFactory.CreateClient(clientHandler);
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromMinutes(15);

            BaseUrl = baseUrl;
        }

        public (bool Success, string ErrorMessage) LogIn(string username, string password)
        {
            try
            {
                //get the login page to obtain ASP.NET anti-forgery tokens
                var loginInitialHtml = client.GetStringAsync("/login/login.aspx").Result;

                var viewState = GetHiddenInputValue(loginInitialHtml, "__VIEWSTATE");
                var viewStateGenerator = GetHiddenInputValue(loginInitialHtml, "__VIEWSTATEGENERATOR");
                var eventValidation = GetHiddenInputValue(loginInitialHtml, "__EVENTVALIDATION");

                var loginContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("__VIEWSTATE", viewState),
                    new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", viewStateGenerator),
                    new KeyValuePair<string, string>("__EVENTVALIDATION", eventValidation),
                    new KeyValuePair<string, string>("txtUserName", username),
                    new KeyValuePair<string, string>("txtPassword", password),
                    //new KeyValuePair<string, string>("chkRememberMe", "on"),
                    new KeyValuePair<string, string>("btnLogin", "Sign+In"),
                ]);

                var loginResponse = client.PostAsync("/login/login.aspx", loginContent).Result;

                if (!loginResponse.IsSuccessStatusCode)
                {
                    return (false, $"Could not log in: {loginResponse}");
                }

                userIdLong = clientHandler.CookieContainer.GetCookies(new Uri(BaseUrl))?["CONDECO"]?.Value.Split("=").Last();
                if (userIdLong == null)
                {
                    return (false, $"Log in details not found. Check your username and password.");
                }

                //get a token for logging into EnterpriseLite
                var entLoginResponseStr = client.GetStringAsync("/EnterpriseLiteLogin.aspx").Result;
                var token = GetHiddenInputValue(entLoginResponseStr, "token");

                var authContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("token", token)
                    ]);

                var authResponse = client.PostAsync("/enterpriselite/auth", authContent).Result;

                if (!authResponse.IsSuccessStatusCode)
                {
                    return (false, $"Could not authenticate: {authResponse}");
                }

                var eliteSessionToken = clientHandler.CookieContainer.GetCookies(new Uri(BaseUrl))?["EliteSession"]?.Value;

                if (eliteSessionToken == null)
                {
                    return (false, $"EliteSession cookie was not retrieved.");
                }

                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", eliteSessionToken);

                //get user info and app settings from the API instead of parsing JavaScript
                var sessionInfoBase64 = client.GetStringAsync("/EnterpriseLite/api/User/GetUserSessionInfo").Result;
                var sessionInfoJson = Encoding.UTF8.GetString(Convert.FromBase64String(sessionInfoBase64.Trim('"')));
                var sessionInfo = JObject.Parse(sessionInfoJson);
                userId = sessionInfo["userId"]?.ToString();
                userFullName = sessionInfo["firstName"] + " " + sessionInfo["lastName"];

                var appSettingsJson = client.GetStringAsync($"/EnterpriseLite/api/Booking/GetAppSetting?accessToken={userIdLong}").Result;
                AppSettings = AppSettingResponse.FromServerResponse(appSettingsJson);

                loginSuccessful = true;
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Exception during login: {ex}");
            }
        }

        public (bool Success, string ErrorMessage) LogIn(string token)
        {
            throw new NotSupportedException($"{nameof(CondecoWeb)} does not support logging in using a token. Instead, try using: --api mobile");
        }

        public string GetFullName()
        {
            return userFullName;
        }

        static string GetHiddenInputValue(string html, string name)
        {
            var match = Regex.Match(html, $@"name=['""]{ Regex.Escape(name)}['""][^>]*value=['""]([^'""]*)['""]");
            return match.Success ? match.Groups[1].Value : throw new Exception($"Could not find hidden input '{name}' in HTML");
        }

        //Book for current user
        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date)
        {
            var currentUser = BookFor.CurrentUser();

            var result = BookRoom(room, date, currentUser);
            return result;
        }

        public Task<HttpResponseMessage> SendBookingRequest(Room room, DateOnly date, BookFor? bookForUser)
        {
            var result = SendBookingRequest(room, [date], bookForUser);
            return result;
        }

        public Task<HttpResponseMessage> SendBookingRequest(Room room, List<DateOnly> dates, BookFor? bookForUser, string? tag = null)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            bookForUser ??= BookFor.CurrentUser();
            var bookForUserStr = bookForUser.ToGeneralFormString();

            var dateStr = dates
                            .Select(date => $"{date:%d/%M/yyyy}")           //todo: Maybe retrieve this format from GetFilteredGridSettings -> RoomSettings -> ShortDateFormat
                            .Select(date => $"{date}_0;{date}_1;")
                            .ToString("");  

            var postStr = $$"""
                {
                    "bookingID": "0",
                    "BookingSource": "1",
                    "countryID": "{{room.CountryId}}",
                    "CultureCode": "en-GB",
                    "datesRequested": "{{dateStr}}",
                    "generalForm": "{{bookForUserStr}}",
                    "groupID": "{{room.GroupId}}",
                    "IsNextDayBookingDeleted": false,
                    "LanguageID": 1,
                    "locationID": "{{room.LocationId}}",
                    "resourceItemID": "{{room.RoomId}}",
                    "TrackDate": "",
                    "UserID": "{{userId}}",
                    "UserLongID": "{{userIdLong}}",
                    "wsTypeId": "2"
                }
                """;

            var post = new HttpRequestMessage(HttpMethod.Post, "/webapi/BookingService/SaveDeskBooking")
            {
                Content = new StringContent(postStr, Encoding.UTF8, "application/json")
            };

            if (tag != null)
            {
                post.Headers.Add("X-Booking-Tag", tag);
            }

            var result = client.SendAsync(post);
            return result;
        }

        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser)
        {
            var httpRequest = SendBookingRequest(room, date, bookForUser);

            string? bookingResponseStr = null;
            try
            {
                var bookingResponse = httpRequest.Result;
                bookingResponseStr = bookingResponse.Content.ReadAsStringAsync().Result;

                bookingResponseStr = bookingResponseStr
                                        .Replace("\"", "")
                                        .Replace("\\\\n", "");
            }
            catch (Exception ex)
            {
                //Catch things such as timeouts
                bookingResponseStr = $"Error while booking: {ex.Message}";
            }



            //The booking response is not reliable. It sometimes says that the booking was successful, when it wasn't.
            //Let's get positive confirmation.

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

        public UpComingBookingsResponse GetUpcomingBookingsResp(DateOnly? fromDate = null, DateOnly? toDate = null)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            fromDate ??= DateOnly.FromDateTime(DateTime.Now.Date);
            toDate ??= fromDate;

            var getUpcomingBookingsUrl = $"/EnterpriseLite/api/Booking/GetUpComingBookings?startDateTime={fromDate.Value.AddDays(-1):yyyy-MM-dd} 14:00:00&endDateTime={toDate.Value:yyyy-MM-dd} 13:59:59";
            var upcomingBookingsJsonArrayStr = client.GetStringAsync(getUpcomingBookingsUrl).Result;

            var result = UpComingBookingsResponse.FromServerResponse(upcomingBookingsJsonArrayStr);
            return result;
        }

        public List<UpcomingBooking> GetUpcomingBookings(DateOnly? fromDate = null, DateOnly? toDate = null)
        {
            var upComingBookings = GetUpcomingBookingsResp(fromDate, toDate);

            var result = upComingBookings
                            .UpComingBookings
                            .Select(booking => new UpcomingBookingWeb()
                            {
                                BookingId = booking.bookingId,
                                BookingItemId = booking.bookingItemId,
                                LocationId = booking.locationId,
                                BookedLocation = booking.bookedLocation,
                                DeskId = booking.bookedResourceItemId,
                                BookingTitle = booking.bookedResourceName,
                                BookingStartDate = booking.startDateTime,
                                BookingEndDate = booking.endDateTime,
                                BookingStatus = booking.bookingStatus,
                                CheckInRequired = booking.bookingMetadata.rules.hdCheckInRequired,

                                BookedForSelf = booking.bookedFor == null,
                                BookedForUserId = booking.bookedFor?.userId,
                                BookedForFullName = booking.bookedFor?.name,

                                OriginalBookingObject = booking
                            })
                            .OfType<UpcomingBooking>()
                            .ToList();

            return result;
        }

        public List<Country> GetCountries()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var result = AppSettings?
                            .WorkspaceTypes
                            .Select(wt => wt.ResourceId)
                            .Distinct()
                            .Select(GetGrid)
                            .SelectMany(grid => grid?.Countries ?? [])
                            .ToList() ?? [];

            return result;
        }

        public List<string> GetCountryNames()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var countries = GetCountries();

            var result = countries
                            .Select(country => country.Name)
                            .ToList();

            return result;
        }

        public GridResponse? GetGrid(string workstationTypeName)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var resourceTypeId = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Name.Equals(workstationTypeName, StringComparison.OrdinalIgnoreCase))?.ResourceId
                                    ?? throw new Exception(
                                        $"Workspace Type not found: {workstationTypeName}{Environment.NewLine}{Environment.NewLine}" +
                                        $"Valid Workspace Types:{Environment.NewLine}" +
                                            $"{AppSettings?.WorkspaceTypes.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");


            var result = GetGrid(resourceTypeId);

            return result;
        }

        public GridResponse? GetGridByWorkstationType(int workstationTypeId)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var resourceTypeId = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Id == workstationTypeId)?.ResourceId
                                    ?? throw new Exception($"Cannot look up the ResourceId for WorkstationTypeId: {workstationTypeId}");

            var result = GetGrid(resourceTypeId);

            return result;
        }

        public GridResponse? GetGrid(int resourceTypeId)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            gridSettingsLimiter.AcquireAsync().AsTask().Wait();

            var postContent = new StringContent($@"{{UserId: {userId}, UserLongId: ""{userIdLong}"", ResourceType: {resourceTypeId}}}", Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync("/webapi/BookingGrid/GetGridSettings", postContent).Result;

            if (!postResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Server returned {(int)postResponse.StatusCode}: {postResponse.ReasonPhrase}");
            }

            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;

            if (Regex.IsMatch(postResponseStr, @"""ResponseCode""\s*:\s*403"))
            {
                Console.WriteLine($"{DateTime.Now}  GetGridSettings returned ResponseCode 403 (ResourceType {resourceTypeId}).");
                return null;
            }

            postResponseStr = JToken.Parse(postResponseStr).ToString();

            var result = GridResponse.FromServerResponse(postResponseStr);
            return result;
        }

        public RoomsResponse? GetRoomResponse(string countryName, string locationName, string groupName, string floorName, string workstationTypeName)
        {
            var grid = GetGrid(workstationTypeName) ?? throw new Exception($"Could not retrieve grid for {nameof(workstationTypeName)}: {workstationTypeName}");

            var country = grid.Countries.FirstOrDefault(cntry => cntry.Name.Equals(countryName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new Exception(
                                $"Country not found: {countryName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid countries:{Environment.NewLine}" +
                                    $"{grid.Countries.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var location = country.Locations.FirstOrDefault(lcation => lcation.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new Exception(
                                $"Location not found: {locationName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid locations:{Environment.NewLine}" +
                                    $"{country.Locations.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var group = location.Groups.FirstOrDefault(grp => grp.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                             ?? throw new Exception(
                                $"Group not found: {groupName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid groups:{Environment.NewLine}" +
                                    $"{location.Groups.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var floor = group.Floors.FirstOrDefault(flr => flr.Name.Equals(floorName, StringComparison.OrdinalIgnoreCase))
                            ?? throw new Exception(
                                $"Floor not found: {floorName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid floors:{Environment.NewLine}" +
                                    $"{group.Floors.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var workspaceType = floor.WorkspaceTypes.FirstOrDefault(wspType => wspType.Name.Equals(workstationTypeName, StringComparison.OrdinalIgnoreCase))
                             ?? throw new Exception(
                                $"Workspace Type not found: {workstationTypeName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid Workspace Types:{Environment.NewLine}" +
                                    $"{floor.WorkspaceTypes.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var workspaceTypeDefinition = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Name.Equals(workstationTypeName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception(
                                $"Workspace Definition not found: {workstationTypeName}{Environment.NewLine}{Environment.NewLine}" +
                                $"Valid Workspace Types:{Environment.NewLine}" +
                                    $"{AppSettings?.WorkspaceTypes.Select(item => $"\t{item.Name}").OrderBy(item => item).ToString(Environment.NewLine)}");

            var postStr = $$"""
                {
                  "CountryId": {{country.Id}},
                  "LocationId": {{location.Id}},
                  "GroupId": {{group.Id}},
                  "FloorId": {{floor.Id}},
                  "WStypeId": {{workspaceType.Id}},
                  "UserLongId": "{{userIdLong}}",
                  "UserId": {{userId}},
                  "ViewType": 2,
                  "LanguageId": 1,
                  "ResourceType": {{workspaceTypeDefinition.ResourceId}},
                  "StartDate": "{{DateTime.Now.Date:yyyy-MM-ddTHH:mm:ss}}"
                }
                """;
            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync("/webapi/BookingGrid/GetFilteredGridSettings", postContent).Result;

            if (!postResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Server returned {(int)postResponse.StatusCode}: {postResponse.ReasonPhrase}");
            }

            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;
            postResponseStr = JToken.Parse(postResponseStr).ToString();

            var result = RoomsResponse.FromServerResponse(country.Id, location.Id, group.Id, floor.Id, postResponseStr);
            return result;
        }

        public List<Room> GetRooms(string countryName, string locationName, string groupName, string floorName, string workstationTypeName)
        {
            var roomResponse = GetRoomResponse(countryName, locationName, groupName, floorName, workstationTypeName);

            return roomResponse?.Rooms ?? [];
        }

        public FilteredBookingsResponse GetBookings(int countryId, int locationId, int groupId, int floorId, int workspaceTypeId, int resourceTypeId, DateTime startDate)
        {
            var post = new
            {
                CountryId = countryId,
                LocationId = locationId,
                GroupId = groupId,
                FloorId = floorId,
                WStypeId = workspaceTypeId,
                UserLongId = userIdLong,
                UserId = userId,
                ViewType = 2,
                LanguageId = 1,
                ResourceType = resourceTypeId,
                StartDate = $"{startDate:yyyy-MM-ddTHH:mm:ss}"
            };
            var postStr = post.ToJson();

            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync($"/webapi/BookingGrid/GetFilteredBookings", postContent).Result;
            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;
            var result = FilteredBookingsResponse.FromServerResponse(postResponseStr);
            return result;
        }

        public bool BookingSuccessful(Room room, DateOnly bookedForDate, BookFor? bookingFor)
        {
            var bookings = GetUpcomingBookings(bookedForDate, bookedForDate);

            var successful = bookings
                                .Where(b => b.DeskId == room.RoomId)
                                .Where(b =>
                                {
                                    var bookingDate = DateOnly.FromDateTime(b.BookingStartDate);
                                    return bookingDate == bookedForDate;
                                })
                                .Where(b =>
                                {
                                    if (bookingFor == null || string.IsNullOrEmpty(bookingFor.UserId))
                                        return b.BookedForSelf;

                                    var expectedName = $"{bookingFor.FirstName} {bookingFor.LastName}";
                                    return b.BookedForFullName == expectedName;
                                })
                                .Any();

            return successful;
        }

        public FindAColleagueSearchResponse FindAColleague(string searchTerm)
        {
            if (!loginSuccessful || userIdLong == null) throw new Exception($"Not yet logged in.");

            //Could also use:
            //https://acme.condecosoftware.com/MobileAPI/DeskBookingService.svc/FindColleague?accessToken={userIdLong}&name={searchTerm}

            var getUpcomingBookingsUrl = $"/webapi/TeamDay/FindAColleagueSearch";

            var postContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("name", searchTerm),
                    new KeyValuePair<string, string>("accessToken", userIdLong)
                    ]);

            var findAColleagueSearchResponse = client.PostAsync(getUpcomingBookingsUrl, postContent).Result;
            var findAColleagueSearchResponseJson = findAColleagueSearchResponse.Content.ReadAsStringAsync().Result;

            var result = FindAColleagueSearchResponse.FromServerResponse(findAColleagueSearchResponseJson);
            return result;
        }

        public List<Model.People.Colleague> FindColleague(string searchTerm)
        {
            var response = FindAColleague(searchTerm);

            var result = response
                                .Colleagues
                                .Select(static colleague => new Model.People.Colleague()
                                {
                                    UserId = $"{colleague.UserID}",
                                    FullName = colleague.FullName,
                                    Email = colleague.Email
                                })
                                .ToList();

            return result;
        }

        public (bool Success, string BookingStatusStr) CheckIn(UpcomingBooking bookingDetails)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            if (!bookingDetails.CheckInRequired)
            {
                Console.WriteLine($"Check-in is not required for bookingId {bookingDetails.BookingId}, bookingItemId {bookingDetails.BookingItemId}.");
                return (false, "Check-in not required");
            }

            if (bookingDetails is not UpcomingBookingWeb upcomingBooking)
            {
                Console.WriteLine($"{nameof(bookingDetails)} must be of type {nameof(UpcomingBookingWeb)} for this function to work.");
                return (false, $"Booking object not supported by {nameof(CondecoWeb)}");
            }

            /*
                From: main.24991d2acaee76da.js

                Desk bookingStatus
	                Reload = -3
	                NoAction = -2
	                Error = -1
	                Booked = 0
	                CheckedIn = 1
	                CheckedOut = 2
	                Transitioning = 3
	                Cancelled = 4

                Room bookingStatus
	                Reload = -3
	                NoAction = -2
	                Error = -1
	                Booked = 0
	                Started = 1
	                Ended = 2
	                Transitioning = 3
	                Cancelled = 4
	                Extend = 5
	                New = 6
	                Pending = 7
	                WaitList = 8
            */

            var query = HttpUtility.ParseQueryString(string.Empty);
            query["ClientId"] = userIdLong;

            upcomingBooking.OriginalBookingObject.bookingStatus = 1;  //required

            var putRequestStr = upcomingBooking.OriginalBookingObject.ToJson();


            var changeBookingStateUrl = $"/EnterpriseLite/api/Booking/ChangeBookingState?{query}";
            var putRequest = new HttpRequestMessage(HttpMethod.Put, changeBookingStateUrl)
            {
                Content = new StringContent(putRequestStr, Encoding.UTF8, "application/json")
            };

            var response = client.Send(putRequest);
            response.EnsureSuccessStatusCode();

            var responseStr = response.Content.ReadAsStringAsync().Result;

            var responseJson = JObject.Parse(responseStr);
            var responseBookingStatus = (string?)responseJson["bookingStatus"] ?? "";

            var bookingSuccessful = !responseBookingStatus.Equals("0");

            return (bookingSuccessful, responseBookingStatus);
        }

        public bool CanBookForOthers(string locationName, string workspaceTypeName, string groupName)
        {
            var grid = GetGrid(workspaceTypeName) ?? throw new Exception($"Could not retrieve booking grid. Exiting.");

            var result = grid.Settings.DeskSettings.BusinessUnitManager == 1;
            return result;
        }

        public bool CanBookForOthersExternal(string locationName, string workspaceTypeName, string groupName)
        {
            var grid = GetGrid(workspaceTypeName) ?? throw new Exception($"Could not retrieve booking grid. Exiting.");

            var result = grid.Settings.DeskSettings.BusinessUnitManager == 1;
            return result;
        }

        public void Dump(string? outputFolder = null)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            outputFolder ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", "dumps", $"dump {DateTime.Now:yyyy-MM-dd HHmm ss}");
            Directory.CreateDirectory(outputFolder);

            var ianaTimezoneStr = TimeZoneConverter.TZConvert.WindowsToIana(TimeZoneInfo.Local.Id);

            GetJson(client, $"/api/systeminfo", Path.Combine(outputFolder, "systeminfo.json"));

            // Legacy fallback for /api/systeminfo (servers that return 404). Contains encrypted auth config (forms vs SSO, OAuth settings).
            GetJson(client, $"/MobileAPI/DeskBookingService.svc/Configuration/GetGlobalSettings", Path.Combine(outputFolder, "GetGlobalSettings.json"));

            DecryptJsonFile(
                Path.Combine(outputFolder, "GetGlobalSettings.json"),
                Path.Combine(outputFolder, "GetGlobalSettings-dec.json"));

            GetJson(client, $"/MobileAPI/MobileService.svc/User/LoginInformationsV2?token={userIdLong}&currentDateTime={DateTime.Now:dd/MM/yyyy}&languageId=1&currentCulture=en-US", Path.Combine(outputFolder, "LoginInformationsV2.json"));
            GetJson(client, $"/MobileAPI/MobileService.svc/GetAllRoles?userlongId={userIdLong}&cultureCode=en-US", Path.Combine(outputFolder, "GetAllRoles.json"));
            GetJson(client, $"/MobileAPI/DeskBookingService.svc/GetAttendanceRecord?accessToken={userIdLong}&startDate={DateTime.Now:dd/MM/yyyy}&endDate={DateTime.Now.AddDays(35):dd/MM/yyyy}&UserId=-1", Path.Combine(outputFolder, "GetAttendanceRecord.json"));
            GetJson(client, $"/MobileAPI/MobileService.svc/MyBookings/ListV2?sessionGuid={userIdLong}&languageId=1&deskStartDate={DateTime.Now.AddMonths(-1):dd/MM/yyyy}&deskEndDate={DateTime.Now.AddDays(35):dd/MM/yyyy}&roomStartDate={DateTime.Now}&timeZoneID={ianaTimezoneStr}&pageSize=100&pageIndex=0", Path.Combine(outputFolder, "MyBookings_ListV2.json"));
            GetJson(client, $"/MobileAPI/MobileService.svc/team/GetMyTeams?userlongId={userIdLong}", Path.Combine(outputFolder, "GetMyTeams.json"));

            GetJson(client, $"/EnterpriseLite/api/Booking/GetAppSetting?accessToken={userIdLong}", Path.Combine(outputFolder, "GetAppSetting.json"));

            var postContent = new StringContent($@"{{""userLongId"":""{userIdLong}""}}", Encoding.UTF8, "application/json");
            GetJson(client, $"/EnterpriseLite/api/User/GetGeoData", postContent, Path.Combine(outputFolder, "GetGeoData.json"));

            var geoInfoFilename = Path.Combine(outputFolder, "ReturnGeoInformation.json");
            postContent = new StringContent($@"{{UserID: {userId}, LongUserId: ""{userIdLong}""}}", Encoding.UTF8, "application/json");
            GetJson(client, $"/webapi/GridDateSelection/ReturnGeoInformation", postContent, geoInfoFilename);
            var geoInfoJsonRaw = File.ReadAllText(geoInfoFilename);
            var outerObj = JObject.Parse(geoInfoJsonRaw);
            var geoInfoJson = (outerObj["d"]?.ToString()) ?? throw new Exception($"Could not deserialize string:{Environment.NewLine}{geoInfoJsonRaw}");
            geoInfoJson = JToken.Parse(geoInfoJson).ToString();
            File.WriteAllText(geoInfoFilename, geoInfoJson);

            //cookies
            var cookiesStr = clientHandler
                                .CookieContainer
                                .GetAllCookies()
                                .ToJson(true);
            File.WriteAllText(Path.Combine(outputFolder, "cookies.json"), cookiesStr);

            var distinctResouceIds = AppSettings?
                                        .WorkspaceTypes
                                        .Select(wt => wt.ResourceId)
                                        .Distinct()
                                        .ToList() ?? [];

            //iterate through all areas to get all rooms
            foreach (var resourceId in distinctResouceIds)
            {
                postContent = new StringContent($@"{{UserId: {userId}, UserLongId: ""{userIdLong}"", ResourceType: {resourceId}}}", Encoding.UTF8, "application/json");
                GetJson(client, $"/webapi/BookingGrid/GetGridSettings", postContent, Path.Combine(outputFolder, $"GetGridSettings - ResourceTypeId {resourceId}.json"));

                GridResponse? grid = null;

                try
                {
                    grid = GetGrid(resourceId);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not retrieve grid for ResourceId: {resourceId}");
                    Console.WriteLine(ex.Message);
                    continue;
                }

                if (grid == null)
                {
                    Console.WriteLine($"Could not retrieve grid for ResourceId: {resourceId}");
                    continue;
                }

                foreach (var country in grid.Countries)
                {
                    foreach (var location in country.Locations)
                    {
                        foreach (var group in location.Groups)
                        {
                            var groupSettingsWithRestrictionsFilename = $"groupSettingsWithRestrictions - {country.Name}, {location.Name}, {group.Name}.json";
                            groupSettingsWithRestrictionsFilename = groupSettingsWithRestrictionsFilename.ReplaceInvalidChars("-");
                            groupSettingsWithRestrictionsFilename = Path.Combine(outputFolder, groupSettingsWithRestrictionsFilename);
                            GetJson(client, $"/MobileAPI/DeskBookingService.svc/groupSettingsWithRestrictions?accessToken={userIdLong}&bookingForUserId=-1&locationId={location.Id}&groupIds={group.Id}", groupSettingsWithRestrictionsFilename);

                            foreach (var floor in group.Floors)
                            {
                                var floorPlanFilename = $"Floorplan - {country.Name}, {location.Name}, {group.Name}, {floor.Name}.json";
                                floorPlanFilename = floorPlanFilename.ReplaceInvalidChars("-");
                                floorPlanFilename = Path.Combine(outputFolder, floorPlanFilename);
                                GetJson(client, $"/MobileAPI/DeskBookingService.svc/floors/Floorplan?accessToken={userIdLong}&locationId={location.Id}&groupId={group.Id}&floorId={floor.Id}&IsV2=true", floorPlanFilename);

                                foreach (var workspaceType in floor.WorkspaceTypes)
                                {
                                    var resId = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Id == workspaceType.Id)?.ResourceId;
                                    if (resId == null) continue;

                                    var post = new
                                    {
                                        CountryId = country.Id,
                                        LocationId = location.Id,
                                        GroupId = group.Id,
                                        FloorId = floor.Id,
                                        WStypeId = workspaceType.Id,
                                        UserLongId = userIdLong,
                                        UserId = userId,
                                        ViewType = 2,
                                        LanguageId = 1,
                                        ResourceType = resId,
                                        StartDate = "{DateTime.Now.Date:yyyy-MM-ddTHH:mm:ss}"
                                    };
                                    var postStr = post.ToJson();

                                    postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

                                    var filename = $"GetFilteredGridSettings - ResourceTypeId {resourceId} - {country.Name}, {location.Name}, {group.Name}, {floor.Name}, {workspaceType.Name}.json";
                                    filename = filename.ReplaceInvalidChars("-");
                                    filename = Path.Combine(outputFolder, filename);
                                    GetJson(client, $"/webapi/BookingGrid/GetFilteredGridSettings", postContent, filename);

                                    filename = $"GetFilteredBookings - ResourceTypeId {resourceId} - {country.Name}, {location.Name}, {group.Name}, {floor.Name}, {workspaceType.Name}.json";
                                    filename = filename.ReplaceInvalidChars("-");
                                    filename = Path.Combine(outputFolder, filename);
                                    GetJson(client, $"/webapi/BookingGrid/GetFilteredBookings", postContent, filename);
                                }
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Dump complete.");
            Console.WriteLine($"Wrote to folder: {outputFolder}");
        }

        public static void DecryptJsonFile(string encryptedFilename, string outputFilename)
        {
            try
            {
                var obj = JObject.Parse(File.ReadAllText(encryptedFilename));

                foreach (var prop in obj.Properties().ToList())
                {
                    if (prop.Value.Type != JTokenType.String) continue;
                    try
                    {
                        obj[prop.Name] = CondecoMobile.LegacyTwoStepDecrypt(prop.Value.ToString());
                    }
                    catch { }
                }

                File.WriteAllText(outputFilename, obj.ToString());
            }
            catch (Exception ex)
            {
                File.WriteAllText(outputFilename, $"Decryption failed: {ex.Message}");
            }
        }

        public static void GetJson(HttpClient client, string url, string saveToFilename)
        {
            Console.WriteLine($"Retrieving: {url}");

            string str;
            try
            {
                str = client.GetStringAsync(url).Result;
                str = JToken.Parse(str).ToString();
            }
            catch (Exception ex)
            {
                str = ex.ToString();
            }

            File.WriteAllText(saveToFilename, str);
        }

        public static void GetJson(HttpClient client, string url, HttpContent postContent, string saveToFilename)
        {
            Console.WriteLine($"Retrieving: {url}");

            string str;
            try
            {
                var postResponse = client.PostAsync(url, postContent).Result;
                str = postResponse.Content.ReadAsStringAsync().Result;
                str = JToken.Parse(str).ToString();
            }
            catch (Exception ex)
            {
                str = ex.ToString();
            }

            File.WriteAllText(saveToFilename, str);
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

        List<DeskSettings> FetchAllDeskSettings()
        {
            return AppSettings?
                        .WorkspaceTypes
                        .Select(wt => wt.ResourceId)
                        .Distinct()
                        .Select(GetGrid)
                        .Select(grid => grid?.Settings?.DeskSettings)
                        .Where(ds => ds != null)
                        .Cast<DeskSettings>()
                        .ToList()
                   ?? [];
        }

        public DayOfWeek GetRolloverDay()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var ds = FetchAllDeskSettings().FirstOrDefault()
                     ?? throw new Exception("No desk settings available.");
            int daysToAdd = ds.IncludeWeekends ? 1 : 2;
            return ds.EndDate.AddDays(daysToAdd).DayOfWeek;
        }

        public DateTime GetBookingWindowStartDate()
        {
            return GetBookingWindow().StartDate;
        }

        public DateTime GetBookingWindowEndDate()
        {
            return GetBookingWindow().EndDate;
        }

        public (DateTime StartDate, DateTime EndDate) GetBookingWindow()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var settings = FetchAllDeskSettings();
            if (settings.Count == 0) throw new Exception("No desk settings available.");

            var startDate = settings
                        .Select(ds => ds.StartDate)
                        .Where(date => date > DateTime.MinValue)
                        .OrderBy(date => date)
                        .FirstOrDefault(DateTime.Now.Date);

            var endDate = settings
                        .Select(ds => ds.EndDate)
                        .Where(date => date > DateTime.MinValue)
                        .OrderByDescending(date => date)
                        .FirstOrDefault(DateTime.Now.Date);

            return (startDate, endDate);
        }
        public void LogOut()
        {
            _ = client.GetStringAsync("/login/login.aspx?logout=1").Result;
        }
    }
}
