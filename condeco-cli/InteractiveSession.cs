using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Model;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using libCondeco.Web;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli
{
    public class InteractiveSession
    {
        private readonly BaseOptions options;
        private readonly CondecoCliConfig config;
        private readonly IHttpClientFactory httpClientFactory;

        public InteractiveSession(BaseOptions options, CondecoCliConfig config, IHttpClientFactory httpClientFactory)
        {
            this.options = options;
            this.config = config;
            this.httpClientFactory = httpClientFactory;
        }

        void CollectBaseUrl()
        {
            while (true)
            {
                Collect(ref config.Account.BaseUrl, "Please enter the url of your Condeco service (example: https://acme.condecosoftware.com): ");

                if (Uri.TryCreate(config.Account.BaseUrl, UriKind.Absolute, out var _))
                {
                    config.Save();
                    return;
                }
                else
                {
                   AnsiConsole.MarkupLine("[red]The URL entered is not valid. Please try again.[/]\n");
                }
            } 
        }

        SsoConfig? detectedSsoConfig;

        void CollectCreds()
        {
            if (options.API == EnumAPI.web)
            {
                CollectUsernameAndPassword();
                return;
            }

            detectedSsoConfig = null;
            var diagnosticOutput = new StringWriter();
            AnsiConsole
                .Status()
                .AutoRefresh(true)
                .Spinner(Spinner.Known.Default)
                .Start("[yellow]Detecting server authentication type...[/]", ctx =>
                {
                    var originalOut = Console.Out;
                    Console.SetOut(diagnosticOutput);
                    try
                    {
                        var mobile = new CondecoMobile(httpClientFactory, config.Account.BaseUrl);
                        detectedSsoConfig = mobile.DetectSso();
                    }
                    catch (Exception ex)
                    {
                        diagnosticOutput.WriteLine($"[SSO] Detection failed: {ex.Message}");
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }
                });

            if (detectedSsoConfig != null)
            {
                AnsiConsole.MarkupLine("[yellow]This server uses SSO authentication.[/]");

                var ssoStr = "SSO (browser sign-in)";
                var tokenStr = "Token (paste an existing token)";

                var loginType = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                        .Title("Log in using: ")
                                                        .AddChoices([ssoStr, tokenStr]));

                if (loginType == tokenStr)
                {
                    detectedSsoConfig = null;
                    Collect(ref config.Account.Token, "Please enter token: ");
                    config.Save();
                }
            }
            else
            {
                var usernameAndPasswordStr = "Username and password";
                var tokenStr = "Token";

                var loginType = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                        .Title("Log in using: ")
                                                        .AddChoices([usernameAndPasswordStr, tokenStr]));

                if (loginType == usernameAndPasswordStr)
                {
                    CollectUsernameAndPassword();
                }
                else if (loginType == tokenStr)
                {
                    Collect(ref config.Account.Token, "Please enter token: ");
                    config.Save();
                }
            }
        }

        void CollectUsernameAndPassword()
        {
            Collect(ref config.Account.Username, "Please enter username: ");
            CollectSecret(ref config.Account.Password, "Please enter password: ");
            config.Save();
        }

        static void Collect(ref string value, string prompt)
        {
            if (string.IsNullOrEmpty(value))
            {
                value = AnsiConsole.Ask<string>(prompt);
            }
            else
            {
                value = AnsiConsole.Ask(prompt, value);
            }
        }

        static void CollectSecret(ref string value, string prompt)
        {
            if (string.IsNullOrEmpty(value))
            {
                value = AnsiConsole.Prompt(
                    new TextPrompt<string>(prompt)
                        .Secret());
            }
            else
            {
                value = AnsiConsole.Prompt(
                    new TextPrompt<string>(prompt)
                        .DefaultValue(value)
                        .Secret());
            }
        }

        static Booking PromptForBookingDetails(ICondeco condeco, Booking? edit)
        {
            const string currentSuffix = " (current)";

            var promptIfRequired = new Func<List<string>, string, string?, string>((Items, Prompt, currentValue) =>
            {
                string selectedValue;
                if (Items.Count == 1)
                {
                    selectedValue = Items[0];
                }
                else
                {
                    var choices = Items.ToList();
                    if (currentValue != null && choices.Remove(currentValue))
                        choices.Insert(0, currentValue + currentSuffix);

                    selectedValue = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .Title(Prompt)
                                                                .AddChoices(choices));

                    if (selectedValue.EndsWith(currentSuffix))
                        selectedValue = selectedValue[..^currentSuffix.Length];
                }

                return selectedValue;
            });


            var countries = condeco.GetCountries();
            var countryNames = countries
                                .Select(country => country.Name)
                                .Distinct()
                                .OrderBy(name => name)
                                .ToList();


            var selectedCountryName = promptIfRequired(countryNames, "Country: ", edit?.Country);




            var selectedCountry = countries
                                    .FirstOrDefault(country => country.Name == selectedCountryName)
                                    ?? throw new Exception($"Country not found: {selectedCountryName}");

            var breadcrumbs = selectedCountryName;



            var locations = selectedCountry.Locations;

            var locationNames = locations
                                .Select(location => location.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedLocation = promptIfRequired(locationNames, "Location: ", edit?.Location);

            breadcrumbs += $" >> {selectedLocation}";




            var groups = locations
                            .FirstOrDefault(location => location.Name == selectedLocation)?
                            .Groups ?? throw new Exception($"No groups found for location: {selectedLocation}");

            var groupNames = groups
                                .Select(group => group.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedGroup = promptIfRequired(groupNames, "Group: ", edit?.Group);

            breadcrumbs += $" >> {selectedGroup}";




            var floors = groups
                            .FirstOrDefault(group => group.Name == selectedGroup)?
                            .Floors ?? throw new Exception($"No floors found for group: {selectedGroup}");

            var floorNames = floors
                                .Select(floor => floor.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedFloor = promptIfRequired(floorNames, "Floor: ", edit?.Floor);

            breadcrumbs += $" >> {selectedFloor}";




            var workspaceTypes = floors
                                    .FirstOrDefault(floor => floor.Name == selectedFloor)?
                                    .WorkspaceTypes ?? throw new Exception($"No workspace types found for floor: {selectedFloor}");

            var workspaceTypeNames = workspaceTypes
                                        .Select(workspaceType => workspaceType.Name)
                                        .OrderBy(name => name)
                                        .ToList();

            var selectedWorkspaceType = promptIfRequired(workspaceTypeNames, "Workspace Type: ", edit?.WorkspaceType);

            breadcrumbs += $" >> {selectedWorkspaceType}";





            var rooms = condeco.GetRooms(
                                    selectedCountryName,
                                    selectedLocation,
                                    selectedGroup,
                                    selectedFloor,
                                    selectedWorkspaceType);


            var roomNames = rooms
                                .Select(room => room.Name)
                                .ToList();

            var selectedRoom = promptIfRequired(roomNames, "Room Name: ", edit?.Desk);




            var daysOfWeek = Enum
                                .GetValues(typeof(DayOfWeek))
                                .Cast<DayOfWeek>()
                                .OrderBy(d => (d - DayOfWeek.Monday + 7) % 7)
                                .Select(day => day.ToString())
                                .ToList();

            var daysPrompt = new MultiSelectionPrompt<string>()
                                .Title("Days: ")
                                .InstructionsText("[grey](Press <space> to selected a day, <enter> to accept)[/]")
                                .AddChoices(daysOfWeek);

            if (edit != null)
                edit.Days.ForEach(day => daysPrompt.Select(day));

            var selectedDays = AnsiConsole.Prompt(daysPrompt).ToList();

            var canBookForOthers = condeco.CanBookForOthers(selectedLocation, selectedWorkspaceType, selectedGroup);
            var canBookForExternalUser = condeco.CanBookForOthersExternal(selectedLocation, selectedWorkspaceType, selectedGroup);

            BookFor bookFor;
            if (canBookForOthers || canBookForExternalUser)
            {
                //This user can book for other users
                bookFor = PromptForUserToBookFor(condeco, canBookForOthers, canBookForExternalUser);
            }
            else
            {
                //This user can only book for themselves
                bookFor = BookFor.CurrentUser();
            }

            Booking result;
            if (edit == null)
            {
                result = new Booking()
                {
                    AutogenName = "",
                    Country = selectedCountryName,
                    Location = selectedLocation,
                    Group = selectedGroup,
                    Floor = selectedFloor,
                    WorkspaceType = selectedWorkspaceType,
                    Desk = selectedRoom,
                    Days = selectedDays,
                    BookFor = bookFor,
                    ExcludeDates = []
                };
            }
            else
            {
                edit.Country = selectedCountryName;
                edit.Location = selectedLocation;
                edit.Group = selectedGroup;
                edit.Floor = selectedFloor;
                edit.WorkspaceType = selectedWorkspaceType;
                edit.Desk = selectedRoom;
                edit.Days = selectedDays;
                edit.BookFor = bookFor;

                result = edit;
            }

            return result;
        }

        public static BookFor PromptForUserToBookFor(ICondeco condeco, bool canBookForOthers, bool canBookForExternalUser)
        {
            if (!canBookForOthers && !canBookForExternalUser)
            {
                var bookFor = BookFor.CurrentUser();
                return bookFor;
            }

            var bookForCurrentUser = $"Current user ({condeco.GetFullName()})";
            var bookForInternalUser = "Internal user";
            var bookForExternalUser = "External user";

            List<string> actionChoices = [bookForCurrentUser];
            if (canBookForOthers)
            {
                actionChoices.Add(bookForInternalUser);
            }

            if (canBookForExternalUser)
            {
                actionChoices.Add(bookForExternalUser);
            }

            AnsiConsole.MarkupLine("");

            BookFor result;
            while (true)
            {
                var selectedAction = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                            .Title("Book for: ")
                                                            .AddChoices(actionChoices));

                if (selectedAction == bookForCurrentUser)
                {
                    result = BookFor.CurrentUser();
                    break;
                }
                else if (selectedAction == bookForInternalUser)
                {
                    while (true)
                    {
                        string searchTerm = "";
                        Collect(ref searchTerm, $"Enter a search term: ");

                        var colleagueSearchResults = condeco.FindColleague(searchTerm);

                        var tryAnotherSearchTermStr = "Try another search term";
                        var cancelStr = "Cancel";

                        if (colleagueSearchResults.Count == 0)
                        {
                            var retrySelection = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .Title("No users found")
                                                                .AddChoices([tryAnotherSearchTermStr, cancelStr]));

                            if (retrySelection == cancelStr)
                            {
                                break;
                            }
                        }
                        else
                        {
                            var colleagueDict = colleagueSearchResults
                                                    .ToDictionary(colleague => $"{colleague.FullName} ({colleague.Email})");

                            var colleagueOptions = colleagueDict
                                                    .Keys
                                                    .ToList();

                            colleagueOptions.Add(tryAnotherSearchTermStr);
                            colleagueOptions.Add(cancelStr);

                            var selectedColleagueOption = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                            .AddChoices(colleagueOptions));

                            if (selectedColleagueOption == cancelStr)
                            {
                                break;
                            }
                            else if (selectedColleagueOption == tryAnotherSearchTermStr)
                            {
                                continue;
                            }
                            else
                            {
                                var selectedColleague = colleagueDict[selectedColleagueOption];
                                var nameTokens = selectedColleague.FullName.Split(' ');

                                //internal user
                                result = new BookFor()
                                {
                                    UserId = $"{selectedColleague.UserId}",
                                    FirstName = nameTokens[0],
                                    LastName = nameTokens.Skip(1).ToString(" "),
                                    EmailAddress = selectedColleague.Email,
                                    IsExternal = "0"
                                };

                                return result;
                            }
                        }
                    }
                }
                else
                {
                    while (true)
                    {
                        var firstName = "";
                        Collect(ref firstName, $"First name: ");

                        var lastName = "";
                        Collect(ref lastName, $"Last name: ");

                        var company = "";
                        Collect(ref company, $"Company: ");

                        var email = "";
                        Collect(ref email, $"Email: ");


                        var acceptStr = "Accept";
                        var reEnterStr = "Re-enter";
                        var cancelStr = "Cancel";

                        var actionSelection = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .AddChoices([acceptStr, reEnterStr, cancelStr]));

                        if (actionSelection == cancelStr)
                        {
                            break;
                        }
                        else if (actionSelection == acceptStr)
                        {
                            //external user
                            result = new BookFor()
                            {
                                UserId = $"0",
                                FirstName = firstName,
                                LastName = lastName,
                                Company = company,
                                EmailAddress = email,
                                IsExternal = "1"
                            };

                            return result;
                        }
                    }
                }
            }

            return result ?? throw new Exception($"User to Book For was not selected");
        }

        public void Run()
        {
            if (string.IsNullOrEmpty(config.Account.BaseUrl)) CollectBaseUrl();
            if (string.IsNullOrEmpty(config.Account.Username) && string.IsNullOrEmpty(config.Account.Token)) CollectCreds();

            ICondeco? condeco = null;

            while (true)
            {
                condeco = Program.BuildCondecoInterface(options, config);

                var loggedIn = false;

                if (detectedSsoConfig != null && condeco is CondecoMobile mobile)
                {
                    AnsiConsole.MarkupLine("[yellow]Starting SSO login...[/]");

                    var originalOut = Console.Out;
                    var ssoDiagnostics = new StringWriter();
                    Console.SetOut(ssoDiagnostics);

                    bool ssoSuccess;
                    string ssoError;
                    SsoTokens? tokens;
                    try
                    {
                        (ssoSuccess, ssoError, tokens) = mobile.SsoLogIn(
                            detectedSsoConfig,
                            config.Account.RefreshToken,
                            () =>
                            {
                                Console.SetOut(originalOut);
                                var code = AnsiConsole.Ask<string>("Paste the authorization code here:");
                                Console.SetOut(ssoDiagnostics);
                                return code;
                            },
                            display: line => { Console.SetOut(originalOut); AnsiConsole.MarkupLine(Markup.Escape(line)); Console.SetOut(ssoDiagnostics); });
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }

                    loggedIn = ssoSuccess;

                    if (loggedIn && tokens != null)
                    {
                        config.Account.Token = tokens.AccessToken;
                        config.Account.RefreshToken = tokens.RefreshToken ?? "";
                        config.Save();
                        AnsiConsole.MarkupLine("[grey][[SSO]] Tokens saved to config.[/]");
                    }
                    else if (!loggedIn)
                    {
                        AnsiConsole.MarkupLine($"[red]SSO login failed: {Markup.Escape(ssoError)}[/]");
                    }

                    detectedSsoConfig = null;
                }
                else
                {
                    AnsiConsole
                        .Status()
                        .AutoRefresh(true)
                        .Spinner(Spinner.Known.Default)
                        .Start("[yellow]Logging in[/]", ctx =>
                        {
                            if (!string.IsNullOrEmpty(config.Account.Username))
                            {
                                (loggedIn, _) = condeco.LogIn(config.Account.Username, config.Account.Password);
                            }
                            else if (!string.IsNullOrEmpty(config.Account.Token))
                            {
                                (loggedIn, _) = condeco.LogIn(config.Account.Token);
                            }
                        });
                }

                if (loggedIn)
                {
                    AnsiConsole.Markup($"[lime]Login successful.[/]");
                    AnsiConsole.MarkupLine("");
                    break;
                }
                else
                {
                    AnsiConsole.MarkupLine($"[red]Login unsuccessful.[/]\n");

                    CollectBaseUrl();
                    CollectCreds();

                    AnsiConsole.Clear();
                }
            }

            if (condeco == null) return;

            while (true)
            {
                AnsiConsole.Clear();

                PrintBookings(config.Bookings, null, condeco);

                var addBooking = "Add a new booking";
                var editBooking = "Edit a booking";
                var deleteBooking = "Delete a booking";
                var quit = "Quit";

                string[] actionChoices;
                if (config.Bookings.Count == 0)
                {
                    actionChoices = [addBooking, quit];
                }
                else
                {
                    actionChoices = [addBooking, editBooking, deleteBooking, quit];
                }

                AnsiConsole.MarkupLine("");
                var selectedAction = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .AddChoices(actionChoices));

                if (selectedAction == addBooking)
                {
                    var newBooking = PromptForBookingDetails(condeco, null);
                    config.Bookings.Add(newBooking);
                    config.Save();
                }

                if (selectedAction == editBooking)
                {
                    var bookingsLookup = config
                                        .Bookings
                                        .ToDictionary(booking => booking.AutogenName);

                    var bookingNames = bookingsLookup.Keys.ToList();
                    bookingNames.Add("Cancel");

                    var selectedBooking = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                    .AddChoices(bookingNames));

                    if (!selectedBooking.Equals("Cancel"))
                    {
                        var booking = bookingsLookup[selectedBooking];
                        AnsiConsole.Clear();
                        PromptForBookingDetails(condeco, booking);
                        config.Save();
                    }
                }

                if (selectedAction == deleteBooking)
                {
                    var bookingNames = config
                                        .Bookings
                                        .Select(booking => booking.AutogenName)
                                        .ToList();
                    bookingNames.Add("Cancel");

                    var selectedBooking = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                    .AddChoices(bookingNames));

                    if (!selectedBooking.Equals("Cancel"))
                    {
                        var bookingNumber = int.Parse(selectedBooking.Split(' ')[1]);

                        config.Bookings.RemoveAt(bookingNumber - 1);
                        config.Save();
                    }
                }

                if (selectedAction == quit)
                {
                    condeco.LogOut();
                    Environment.Exit(0);
                }
            }
        }

        private static void PrintBookings(List<Booking> bookings, string? highlightBooking, ICondeco condeco)
        {
            if (bookings.Count == 0) return;

            var table = new Table()
                            .AddColumns(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.AutogenName}[/]" : booking.AutogenName).ToArray())
                                .AddRow(bookings.Select(booking =>
                                {
                                    var bookingFor = booking.GetBookingForFullName(condeco);

                                    var cells = booking.AutogenName.Equals(highlightBooking) ?
                                        $"[yellow]{bookingFor}[/]" :
                                        $"{bookingFor}";
                                    return cells;

                                }).ToArray())
                                .AddRow(bookings.Select(_ => "").ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Country}[/]" : booking.Country).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Location}[/]" : booking.Location).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Group}[/]" : booking.Group).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Floor}[/]" : booking.Floor).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.WorkspaceType}[/]" : booking.WorkspaceType).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Desk}[/]" : booking.Desk).ToArray())
                                .AddRow(bookings.Select(_ => "").ToArray());

            var daysOfWeek = Enum
                                .GetValues(typeof(DayOfWeek))
                                .Cast<DayOfWeek>()
                                .OrderBy(d => (d - DayOfWeek.Monday + 7) % 7)
                                .ToList();

            var longestDayOfWeek = daysOfWeek.Max(dow => dow.ToString().Length);

            daysOfWeek
                .ForEach(dayOfWeek =>
                {
                    var rowStrings = bookings
                                    .Select(booking =>
                                    {
                                        var dayStr = dayOfWeek.ToString();

                                        if (booking.Days.Contains(dayOfWeek.ToString()))
                                        {
                                            dayStr = dayStr.PadRight(longestDayOfWeek + 3);
                                            dayStr += "✓";  //√
                                        }

                                        if (booking.AutogenName.Equals(highlightBooking)) dayStr = $"[yellow]{dayStr}[/]";

                                        return dayStr;
                                    })
                                    .ToArray();

                    table.AddRow(rowStrings);
                });

            AnsiConsole.Write(table);
        }
    }
}
