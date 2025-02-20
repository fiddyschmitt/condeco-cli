using condeco_cli.Extensions;
using HtmlAgilityPack;
using libCondeco.Model.Queries;
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
        HttpClientHandler clientHandler;
        HttpClient client;
        bool loginSuccessful = false;

        public string BaseUrl { get; }

        //Session-related info
        string? userId;
        string? userIdLong;

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
            var bookingResponseStr = bookingResponse.Content.ReadAsStringAsync().Result;

            var condecoBookingResponse = BookingResponse.FromServerResponse(bookingResponseStr);

            if (bookingResponse.StatusCode == HttpStatusCode.Created) return (true, condecoBookingResponse);

            return (false, condecoBookingResponse);
        }

        public GridResponse? GetGrid()
        {
            if (!loginSuccessful) throw new Exception($"Not yet logged in.");

            var userLongId = clientHandler.CookieContainer.GetCookies(new Uri(BaseUrl))?["CONDECO"]?.Value.Split("=").Last();
            var postContent = new StringContent($@"{{UserId: {userId}, UserLongId: ""{userLongId}"", ResourceType: 128}}", Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync("/webapi/BookingGrid/GetGridSettings", postContent).Result;

            if (!postResponse.IsSuccessStatusCode)
            {
                return null;
            }

            var postResponseStr = postResponse.Content.ReadAsStringAsync().Result;
            postResponseStr = JToken.Parse(postResponseStr).ToString();

            var result = GridResponse.FromServerResponse(postResponseStr);
            return result;
        }

        public RoomsResponse? GetRooms(GridResponse grid, string countryName, string locationName, string groupName, string floorName, string workstationTypeName)
        {
            var country = grid.Countries.FirstOrDefault(cntry => cntry.Name.Equals(countryName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Country not found: {countryName}");

            var location = country.Locations.FirstOrDefault(lcation => lcation.Name.Equals(locationName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Location not found: {locationName}");

            var group = location.Groups.FirstOrDefault(grp => grp.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Group not found: {groupName}");

            var floor = group.Floors.FirstOrDefault(flr => flr.Name.Equals(floorName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Floor not found: {floorName}");

            var workspaceType = floor.WorkspaceTypes.FirstOrDefault(wspType => wspType.Name.Equals(workstationTypeName, StringComparison.OrdinalIgnoreCase)) ?? throw new Exception($"Workspace Type not found: {workstationTypeName}");

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
                  "ResourceType": 128,
                  "StartDate": "{{DateTime.Now.Date:yyyy-MM-ddTHH:mm:ss}}"
                }
                """;
            var postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

            var postResponse = client.PostAsync("/webapi/BookingGrid/GetFilteredGridSettings", postContent).Result;

            if (!postResponse.IsSuccessStatusCode)
            {
                return null;
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

            postContent = new StringContent($@"{{UserId: {userId}, UserLongId: ""{userIdLong}"", ResourceType: 128}}", Encoding.UTF8, "application/json");
            GetJson(client, $"/webapi/BookingGrid/GetGridSettings", postContent, Path.Combine(outputFolder, "GetGridSettings.json"));

            var grid = GetGrid();
            if (grid != null)
            {
                //iterate through all areas to get all rooms
                foreach (var country in grid.Countries)
                    foreach (var location in country.Locations)
                        foreach (var group in location.Groups)
                            foreach (var floor in group.Floors)
                                foreach (var workspaceType in floor.WorkspaceTypes)
                                {
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
                                                      "ResourceType": 128,
                                                      "StartDate": "{{DateTime.Now.Date:yyyy-MM-ddTHH:mm:ss}}"
                                                    }
                                                    """;
                                    postContent = new StringContent(postStr, Encoding.UTF8, "application/json");

                                    var filename = $"GetFilteredGridSettings - {country.Name}, {location.Name}, {group.Name}, {floor.Name}, {workspaceType.Name}.json";
                                    filename = filename.ReplaceInvalidChars("-");
                                    Path.Combine(outputFolder, filename);

                                    GetJson(client, $"/webapi/BookingGrid/GetFilteredGridSettings", postContent, Path.Combine(outputFolder, filename));
                                }
            }


            //cookies
            var cookiesStr = clientHandler
                                .CookieContainer
                                .GetAllCookies()
                                .ToJson(true);
            File.WriteAllText(Path.Combine(outputFolder, "cookies.json"), cookiesStr);
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
            var logoutResult = client.GetStringAsync("/login/login.aspx?logout=1").Result;
        }
    }
}
