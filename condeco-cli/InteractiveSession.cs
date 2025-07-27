using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Model;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.Space;
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
        private readonly CondecoCliConfig config;

        public InteractiveSession(CondecoCliConfig config)
        {
            this.config = config;
        }

        void CollectBaseUrl()
        {
            Collect(ref config.Account.BaseUrl, "Please enter the url of your Condeco service (example: https://acme.condecosoftware.com): ");
            config.Save();
        }

        void CollectUsername()
        {
            Collect(ref config.Account.Username, "Please enter username: ");
            config.Save();
        }

        void CollectPassword()
        {
            Collect(ref config.Account.Password, "Please enter password: ");
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

        static Booking PromptForBookingDetails(CondecoWeb condecoWeb, Booking? edit)
        {
            var promptIfRequired = new Func<List<string>, string, string>((Items, Prompt) =>
            {
                string selectedValue;
                if (Items.Count == 1)
                {
                    selectedValue = Items[0];
                }
                else
                {
                    selectedValue = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .Title(Prompt)
                                                                .AddChoices(Items));
                }

                return selectedValue;
            });


            var countries = condecoWeb.GetCountries();
            var countryNames = countries
                                .Select(country => country.Name)
                                .Distinct()
                                .OrderBy(name => name)
                                .ToList();


            var selectedCountryName = promptIfRequired(countryNames, "Country: ");




            var selectedCountry = countries
                                    .FirstOrDefault(country => country.Name == selectedCountryName)
                                    ?? throw new Exception($"Country not found: {selectedCountryName}");

            var breadcrumbs = selectedCountryName;



            var locations = selectedCountry.Locations;

            var locationNames = locations
                                .Select(location => location.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedLocation = promptIfRequired(locationNames, "Location: ");

            breadcrumbs += $" >> {selectedLocation}";




            var groups = locations
                            .FirstOrDefault(location => location.Name == selectedLocation)?
                            .Groups ?? throw new Exception($"No groups found for location: {selectedLocation}");

            var groupNames = groups
                                .Select(group => group.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedGroup = promptIfRequired(groupNames, "Group: ");

            breadcrumbs += $" >> {selectedGroup}";




            var floors = groups
                            .FirstOrDefault(group => group.Name == selectedGroup)?
                            .Floors ?? throw new Exception($"No floors found for group: {selectedGroup}");

            var floorNames = floors
                                .Select(floor => floor.Name)
                                .OrderBy(name => name)
                                .ToList();

            var selectedFloor = promptIfRequired(floorNames, "Floor: ");

            breadcrumbs += $" >> {selectedFloor}";




            var workspaceTypes = floors
                                    .FirstOrDefault(floor => floor.Name == selectedFloor)?
                                    .WorkspaceTypes ?? throw new Exception($"No workspace types found for floor: {selectedFloor}");

            var workspaceTypeNames = workspaceTypes
                                        .Select(workspaceType => workspaceType.Name)
                                        .OrderBy(name => name)
                                        .ToList();

            var selectedWorkspaceType = promptIfRequired(workspaceTypeNames, "Workspace Type: ");

            breadcrumbs += $" >> {selectedWorkspaceType}";





            var rooms = condecoWeb.GetRooms(
                                    selectedCountry.Grid ?? throw new Exception($"Grid not present for country: {selectedCountry.Name}"),
                                    selectedCountryName,
                                    selectedLocation,
                                    selectedGroup,
                                    selectedFloor,
                                    selectedWorkspaceType)
                                    ?.Rooms ?? throw new Exception($"No rooms found for: {breadcrumbs}");


            var roomNames = rooms
                                .Select(room => room.Name)
                                .ToList();

            var selectedRoom = promptIfRequired(roomNames, "Room Name: ");




            var daysOfWeek = Enum
                                .GetValues(typeof(DayOfWeek))
                                .Cast<DayOfWeek>()
                                .OrderBy(d => (d - DayOfWeek.Monday + 7) % 7)
                                .Select(day => day.ToString())
                                .ToList();

            var selectedDays = AnsiConsole
                                    .Prompt(
                                        new MultiSelectionPrompt<string>()
                                            .Title("Days: ")
                                            .InstructionsText("[grey](Press <space> to selected a day, <enter> to accept)[/]")
                                            .AddChoices(daysOfWeek))
                                    .ToList();

            BookFor bookFor;

            if (selectedCountry.Grid.Settings.DeskSettings.BusinessUnitManager == 1)
            {
                //This user can book for other users
                bookFor = PromptForUserToBookFor(condecoWeb);
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

        public static BookFor PromptForUserToBookFor(CondecoWeb condecoWeb)
        {
            var bookForCurrentUser = $"Current user ({condecoWeb.userFullName})";
            var bookForInternalUser = "Internal user";
            var bookForExternalUser = "External user";

            string[] actionChoices = [bookForCurrentUser, bookForInternalUser, bookForExternalUser];

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

                        var searchResults = condecoWeb.FindAColleague(searchTerm);

                        var tryAnotherSearchTermStr = "Try another search term";
                        var cancelStr = "Cancel";

                        if (searchResults.Colleagues.Length == 0)
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
                            var colleagueDict = searchResults
                                                    .Colleagues
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
                                    UserId = $"{selectedColleague.UserID}",
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
            if (string.IsNullOrEmpty(config.Account.Username)) CollectUsername();
            if (string.IsNullOrEmpty(config.Account.Password)) CollectPassword();

            CondecoWeb? condecoWeb = null;

            while (true)
            {
                condecoWeb = new CondecoWeb(config.Account.BaseUrl);

                var loggedIn = false;

                AnsiConsole
                    .Status()
                    .AutoRefresh(true)
                    .Spinner(Spinner.Known.Default)
                    .Start("[yellow]Logging in[/]", ctx =>
                    {
                        (loggedIn, _) = condecoWeb.LogIn(
                                                        config.Account.Username,
                                                        config.Account.Password);
                    });

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
                    CollectUsername();
                    CollectPassword();

                    AnsiConsole.Clear();
                }
            }

            if (condecoWeb == null) return;

            while (true)
            {
                AnsiConsole.Clear();

                PrintBookings(config.Bookings, null, condecoWeb.userFullName);

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
                    //edit is not very helpful because SpectreConsole's SelectPrompt doesn't support Default Values. See: https://github.com/spectreconsole/spectre.console/issues/508
                    actionChoices = [addBooking, deleteBooking, quit];
                }

                AnsiConsole.MarkupLine("");
                var selectedAction = AnsiConsole.Prompt(new SelectionPrompt<string>()
                                                                .AddChoices(actionChoices));

                if (selectedAction == addBooking)
                {
                    var newBooking = PromptForBookingDetails(condecoWeb, null);
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
                        PrintBookings(config.Bookings, booking.AutogenName, condecoWeb.userFullName);
                        PromptForBookingDetails(condecoWeb, booking);
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
                    condecoWeb.LogOut();
                    Environment.Exit(0);
                }
            }
        }

        private static void PrintBookings(List<Booking> bookings, string? highlightBooking, string? currentUserFullName)
        {
            if (bookings.Count == 0) return;

            var table = new Table()
                            .AddColumns(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.AutogenName}[/]" : booking.AutogenName).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Country}[/]" : booking.Country).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Location}[/]" : booking.Location).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Group}[/]" : booking.Group).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Floor}[/]" : booking.Floor).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.WorkspaceType}[/]" : booking.WorkspaceType).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ? $"[yellow]{booking.Desk}[/]" : booking.Desk).ToArray())
                                .AddRow(bookings.Select(booking => booking.AutogenName.Equals(highlightBooking) ?
                                    $"[yellow]{(booking.BookFor == null || string.IsNullOrEmpty(booking.BookFor.UserId) ? currentUserFullName : booking.BookFor.FirstName + " " + booking.BookFor.LastName)}[/]" :
                                    $"{(booking.BookFor == null || string.IsNullOrEmpty(booking.BookFor.UserId) ? currentUserFullName : booking.BookFor.FirstName + " " + booking.BookFor.LastName)}").ToArray())
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
