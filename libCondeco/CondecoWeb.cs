using HtmlAgilityPack;
using libCondeco.Extensions;
using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using libCondeco.Model.Web;
using libCondeco.Model.Web.Responses;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Text;
using System.Web;

namespace libCondeco
{
    public class CondecoWeb : ICondeco
    {
        readonly HttpClientHandler clientHandler;
        readonly HttpClient client;
        bool loginSuccessful = false;

        public string BaseUrl { get; }

        //Session-related info
        string? userId;
        string? userIdLong;
        string userFullName = string.Empty;
        AppSettingResponse? AppSettings;     //app settings as provided by web server

        public CondecoWeb(string baseUrl)
        {
            clientHandler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer()
            };

            client = new HttpClient(clientHandler)
            {
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };
            BaseUrl = baseUrl;
        }

        public (bool Success, string ErrorMessage) LogIn(string username, string password)
        {
            try
            {
                //open the login page, to get the ASP.NET session vars
                var loginInitialHtml = client.GetStringAsync("/login/login.aspx").Result;
                var doc = new HtmlDocument();
                doc.LoadHtml(loginInitialHtml);

                //read the ASP.NET session vars from the HTML
                var viewState = doc.DocumentNode.SelectSingleNode($"//input[@name='__VIEWSTATE']").GetAttributeValue("value", "");
                var viewStateGenerator = doc.DocumentNode.SelectSingleNode($"//input[@name='__VIEWSTATEGENERATOR']").GetAttributeValue("value", "");
                var eventValidation = doc.DocumentNode.SelectSingleNode($"//input[@name='__EVENTVALIDATION']").GetAttributeValue("value", "");

                //login into condeco
                var loginContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("__EVENTTARGET", ""),
                    new KeyValuePair<string, string>("__EVENTARGUMENT", ""),
                    new KeyValuePair<string, string>("__VIEWSTATE", viewState),
                    new KeyValuePair<string, string>("__VIEWSTATEGENERATOR", viewStateGenerator),
                    new KeyValuePair<string, string>("__EVENTVALIDATION", eventValidation),

                    new KeyValuePair<string, string>("txtUserName", username),
                    new KeyValuePair<string, string>("txtPassword", password),
                    //new KeyValuePair<string, string>("chkRememberMe", "on"),
                    new KeyValuePair<string, string>("btnLogin", "Sign+In"),
                ]);

                var loginResponse = client.PostAsync("/login/login.aspx", loginContent).Result;
                var loginResponseStr = loginResponse.Content.ReadAsStringAsync().Result;

                if (!loginResponse.IsSuccessStatusCode)
                {
                    return (false, $"Could not log in: {loginResponse}");
                }

                //retrieve the UserId from the html
                userId = loginResponseStr.Split("var int_userID = ", StringSplitOptions.None).Last().Split(";", StringSplitOptions.None).First();
                if (userId == null)
                {
                    return (false, $"Could not extract UserId from HTML");
                }

                userFullName = loginResponseStr.Split("var userFullName = '", StringSplitOptions.None).Last().Split("';", StringSplitOptions.None).First();

                //retrieve the userIdLong from the cookie
                userIdLong = clientHandler.CookieContainer.GetCookies(new Uri(BaseUrl))?["CONDECO"]?.Value.Split("=").Last();
                if (userIdLong == null)
                {
                    return (false, $"CONDECO cookie was not retrieved.");
                }

                //get a token for logging into the Enterprise
                var entLoginResponseStr = client.GetStringAsync("/EnterpriseLiteLogin.aspx").Result;
                doc.LoadHtml(entLoginResponseStr);
                var token = doc.DocumentNode.SelectSingleNode($"//input[@name='token']").GetAttributeValue("value", "");

                //authenticate into Enterprise
                var authContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("token", token)
                    ]);

                //this results in the 'EliteSession' cookie being retrieved.
                //It's also possible to retrieve the EliteSession token using:
                //POST to /ServiceCall.aspx/GetJsonToken
                //GET to  /EnterpriseLite/api/User/GetUserSessionInfo
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

                //use the eliteSessionToken for future requests
                client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", eliteSessionToken);

                //collect the App Settings
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

        //Book for current user
        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date)
        {
            var currentUser = BookFor.CurrentUser();

            var result = BookRoom(room, date, currentUser);
            return result;
        }

        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            bookForUser ??= BookFor.CurrentUser();
            var bookForUserStr = bookForUser.ToGeneralFormString();

            var dateStr = date.ToString("%d/%M/yyyy");  //todo: Maybe retrieve this format from GetFilteredGridSettings -> RoomSettings -> ShortDateFormat

            var postStr = $$"""
                {
                    "bookingID": "0",
                    "BookingSource": "1",
                    "countryID": "{{room.CountryId}}",
                    "CultureCode": "en-GB",
                    "datesRequested": "{{dateStr}}_0;{{dateStr}}_1;",
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

            var content = new StringContent(postStr, Encoding.UTF8, "application/json");

            var bookingResponse = client.PostAsync("/webapi/BookingService/SaveDeskBooking", content).Result;
            var bookingResponseStr = bookingResponse.Content.ReadAsStringAsync().Result;

            bookingResponseStr = bookingResponseStr
                                        .Replace("\"", "")
                                        .Replace("\\\\n", "");

            //The booking response is not reliable. It sometimes says that the booking was successful, when it wasn't.
            //Let's get positive confirmation.

            var resourceTypeId = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Id == room.WSTypeId)?.ResourceId
                                    ?? throw new Exception($"Cannot look up the ResourceId for WorkstationTypeId: {room.WSTypeId}");

            var successful = BookingSuccessful(room.CountryId, room.LocationId, room.GroupId, room.FloorId, room.WSTypeId, resourceTypeId, room.RoomId, date, bookForUser);

            if (successful)
            {
                try
                {
                    //Sometimes this contains "You have already reserved this workspace type for this time slot"
                    var bookingResponseObj = bookingResponseStr.ToObject<BookingResponse>();
                    return (true, bookingResponseObj);
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

        public UpComingBookingsResponse GetUpcomingBookings(DateOnly date)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var getUpcomingBookingsUrl = $"/EnterpriseLite/api/Booking/GetUpComingBookings?startDateTime={date.AddDays(-1):yyyy-MM-dd} 14:00:00&endDateTime={date:yyyy-MM-dd} 13:59:59";
            var upcomingBookingsJsonArrayStr = client.GetStringAsync(getUpcomingBookingsUrl).Result;

            var result = UpComingBookingsResponse.FromServerResponse(upcomingBookingsJsonArrayStr);
            return result;
        }

        public List<UpcomingBooking> GetUpcomingBookings()
        {
            var checkinDate = DateOnly.FromDateTime(DateTime.Now.Date);
            var upComingBookings = GetUpcomingBookings(checkinDate);

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

            var postContent = new StringContent($@"{{UserId: {userId}, UserLongId: ""{userIdLong}"", ResourceType: {resourceTypeId}}}", Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync("/webapi/BookingGrid/GetGridSettings", postContent).Result;

            if (!postResponse.IsSuccessStatusCode)
            {
                throw new Exception($"Server returned {(int)postResponse.StatusCode}: {postResponse.ReasonPhrase}");
            }

            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;
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

        public bool BookingSuccessful(int countryId, int locationId, int groupId, int floorId, int workspaceTypeId, int resourceTypeId, int roomId, DateOnly bookedForDate, BookFor bookingFor)
        {
            var bookings = GetBookings(countryId, locationId, groupId, floorId, workspaceTypeId, resourceTypeId, bookedForDate.ToDateTime(TimeOnly.MinValue));

            var successful = bookings
                                .Meetings
                                .Where(booking =>
                                {
                                    var startDate = DateOnly.FromDateTime(booking.Start);
                                    var endDateTime = DateOnly.FromDateTime(booking.End);

                                    var matchesDate = bookedForDate > startDate && bookedForDate <= endDateTime;
                                    return matchesDate;

                                })
                                .Where(booking => booking.RoomId == roomId)
                                .Where(booking =>
                                {
                                    var matchesUser = true;

                                    if (bookingFor.IsExternal == "1")
                                    {
                                        matchesUser &= booking.AdditionalInfo.FullName == $"{bookingFor.FirstName} {bookingFor.LastName}";
                                    }
                                    else
                                    {
                                        if (bookingFor.UserId == "")
                                        {
                                            matchesUser &= "" + booking.AdditionalInfo.BookingOwnerUserID == userId;
                                        }
                                        else
                                        {
                                            matchesUser &= "" + booking.AdditionalInfo.BookingOwnerUserID == bookingFor.UserId;
                                        }
                                    }

                                    return matchesUser;
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

        public DateTime GetBookingWindowStartDate()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var result = AppSettings?
                            .WorkspaceTypes
                            .Select(wt => wt.ResourceId)
                            .Distinct()
                            .Select(GetGrid)
                            .Select(grid => grid?.Settings.DeskSettings.StartDate ?? DateTime.Now.Date)
                            .Where(date => date > DateTime.MinValue)
                            .OrderBy(date => date)
                            .FirstOrDefault() ?? DateTime.Now.Date;

            return result;
        }

        public DateTime GetBookingWindowEndDate()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var result = AppSettings?
                            .WorkspaceTypes
                            .Select(wt => wt.ResourceId)
                            .Distinct()
                            .Select(GetGrid)
                            .Select(grid => grid?.Settings.DeskSettings.EndDate ?? DateTime.Now.Date)
                            .Where(date => date > DateTime.MinValue)
                            .OrderByDescending(date => date)
                            .FirstOrDefault() ?? DateTime.Now.Date;

            return result;
        }
        public void LogOut()
        {
            _ = client.GetStringAsync("/login/login.aspx?logout=1").Result;
        }
    }
}
