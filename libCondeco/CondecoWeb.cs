using HtmlAgilityPack;
using libCondeco.Extensions;
using libCondeco.Model.Responses;
using libCondeco.Model.Space;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Text;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace libCondeco
{
    public class CondecoWeb
    {
        readonly HttpClientHandler clientHandler;
        readonly HttpClient client;
        bool loginSuccessful = false;

        public string BaseUrl { get; }

        //Session-related info
        string? userId;
        string? userIdLong;
        GetAppSettingResponse? AppSettings;     //app settings as provided by web server

        public CondecoWeb(string baseUrl)
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
                AppSettings = GetAppSettingResponse.FromServerResponse(appSettingsJson);


                loginSuccessful = true;
                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Exception during login: {ex}");
            }
        }

        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var dateStr = date.ToString("dd/MM/yyyy");  //todo: Maybe retrieve this format from GetFilteredGridSettings -> RoomSettings -> ShortDateFormat

            var postStr = $$"""
                {
                    "accessToken": "{{userIdLong}}",
                    "datesInformation": [
                        {
                            "bookingType": "3",
                            "startDate": "{{dateStr}}"
                        }
                    ],
                    "deskID": {{room.RoomId}},
                    "floorID": {{room.FloorId}},
                    "groupID": {{room.GroupId}},
                    "locationID": {{room.LocationId}},
                    "pagingEnabled": false,
                    "wsType": {{room.WSTypeId}}
                }
                """;

            var content = new StringContent(postStr, Encoding.UTF8, "application/json");


            var bookingResponse = client.PostAsync("/EnterpriseLite/api/Desk/Book", content).Result;

            if (bookingResponse.StatusCode == HttpStatusCode.InternalServerError)
            {
                //sometimes the Book API does not work. Try an alternative.

                dateStr = date.ToString("%d/%M/yyyy");

                postStr = $$"""
                {
                    "bookingID": "0",
                    "BookingSource": "1",
                    "countryID": "{{room.CountryId}}",
                    "CultureCode": "en-GB",
                    "datesRequested": "{{dateStr}}_0;{{dateStr}}_1;",
                    "generalForm": "fkUserID~¬firstName~¬lastName~¬company~¬emailAddress~¬telephone~¬isExternal~0¬notifyByPhone~0¬notifyByEmail~0¬notifyBySMS~¬",
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

                content = new StringContent(postStr, Encoding.UTF8, "application/json");

                bookingResponse = client.PostAsync("/webapi/BookingService/SaveDeskBooking", content).Result;
                var bookingResponseStr = bookingResponse.Content.ReadAsStringAsync().Result;

                bookingResponseStr = bookingResponseStr
                                            .Replace("\"", "")
                                            .Replace("\\\\n", "");

                if (int.TryParse(bookingResponseStr, out var bookingId))
                {
                    var condecoBookingResponse = new BookingResponse()
                    {
                        CallResponse = new CallResponse()
                        {
                            ResponseCode = "Okay",
                        },
                        CreatedBookings = [
                            new()
                            {
                                BookingID = bookingId,
                            }
                        ]
                    };

                    return (true, condecoBookingResponse);
                }
                else
                {
                    var condecoBookingResponse = new BookingResponse()
                    {
                        CallResponse = new CallResponse()
                        {
                            ResponseCode = "Custom",
                            ResponseMessage = $"{bookingResponseStr}"
                        }
                    };

                    return (false, condecoBookingResponse);
                }
            }
            else
            {
                var bookingResponseStr = bookingResponse.Content.ReadAsStringAsync().Result;

                var condecoBookingResponse = BookingResponse.FromServerResponse(bookingResponseStr);

                if (bookingResponse.StatusCode == HttpStatusCode.Created) return (true, condecoBookingResponse);

                if (string.IsNullOrEmpty(condecoBookingResponse.CallResponse.ResponseCode))
                {
                    condecoBookingResponse.CallResponse.ResponseCode = $"{(int)bookingResponse.StatusCode}";
                    condecoBookingResponse.CallResponse.ResponseMessage = $"{bookingResponse.ReasonPhrase}";
                }
                return (false, condecoBookingResponse);
            }
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

        public RoomsResponse? GetRooms(GridResponse grid, string countryName, string locationName, string groupName, string floorName, string workstationTypeName)
        {
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

        public void Dump(string? outputFolder = null)
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            outputFolder ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "output", "dumps", $"dump {DateTime.Now:yyyy-MM-dd HHmm ss}");
            Directory.CreateDirectory(outputFolder);

            GetJson(client, $"/EnterpriseLite/api/Booking/GetAppSetting?accessToken={userIdLong}", Path.Combine(outputFolder, "GetAppSetting.json"));

            var postContent = new StringContent($@"{{""userLongId"":""{userIdLong}""}}", Encoding.UTF8, "application/json");
            GetJson(client, $"/EnterpriseLite/api/User/GetGeoData", postContent, Path.Combine(outputFolder, "GetGeoData.json"));

            postContent = new StringContent($@"{{UserID: {userId}, LongUserId: ""{userIdLong}""}}", Encoding.UTF8, "application/json");
            GetJson(client, $"/webapi/GridDateSelection/ReturnGeoInformation", postContent, Path.Combine(outputFolder, "ReturnGeoInformation.json"));

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
                    foreach (var location in country.Locations)
                        foreach (var group in location.Groups)
                            foreach (var floor in group.Floors)
                                foreach (var workspaceType in floor.WorkspaceTypes)
                                {
                                    var resId = AppSettings?.WorkspaceTypes.FirstOrDefault(wt => wt.Id == workspaceType.Id)?.ResourceId;
                                    if (resId == null) continue;

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
                                                      "ResourceType": {{resId}},
                                                      "StartDate": "{{DateTime.Now.Date:yyyy-MM-ddTHH:mm:ss}}"
                                                    }
                                                    """;
                                    postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

                                    var filename = $"GetFilteredGridSettings - ResourceTypeId {resourceId} - {country.Name}, {location.Name}, {group.Name}, {floor.Name}, {workspaceType.Name}.json";
                                    filename = filename.ReplaceInvalidChars("-");
                                    Path.Combine(outputFolder, filename);

                                    GetJson(client, $"/webapi/BookingGrid/GetFilteredGridSettings", postContent, Path.Combine(outputFolder, filename));
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

        public void LogOut()
        {
            _ = client.GetStringAsync("/login/login.aspx?logout=1").Result;
        }
    }
}
