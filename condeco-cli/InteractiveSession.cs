using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Model;
using condeco_cli.Scheduling;
using condeco_cli.Updating;
using libCondeco;
using libCondeco.EpturaOne;
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
            PlatformConfig? detectedPlatform = null;
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
                        detectedPlatform = mobile.DetectPlatform();
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

            if (detectedPlatform != null)
            {
                AnsiConsole.MarkupLine("[lime]This tenant is on the Eptura One platform.[/]");
                AnsiConsole.MarkupLine($"  Ping URL: {Markup.Escape(detectedPlatform.PingUrl)}");
                AnsiConsole.MarkupLine($"  Auth URL: {Markup.Escape(detectedPlatform.AuthUrl)}");
                if (!string.IsNullOrEmpty(detectedPlatform.BaseUrl))
                {
                    AnsiConsole.MarkupLine($"  Base URL: {Markup.Escape(detectedPlatform.BaseUrl)}");
                }
                AnsiConsole.MarkupLine("");
            }

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
                                .GetValues<DayOfWeek>()
                                .Cast<DayOfWeek>()
                                .OrderBy(d => (d - DayOfWeek.Monday + 7) % 7)
                                .Select(day => day.ToString())
                                .ToList();

            var daysPrompt = new MultiSelectionPrompt<string>()
                                .Title("Days: ")
                                .InstructionsText("[grey](Press <space> to selected a day, <enter> to accept)[/]")
                                .AddChoices(daysOfWeek);

            edit?.Days.ForEach(day => daysPrompt.Select(day));

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
            if (config.UpdateSettings.AutoUpdate)
            {
                var updated = HandleCheckForUpdates(pauseOnNoUpdate: false);
                if (updated)
                {
                    Environment.Exit(0);
                }
            }

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

            var configSlug = Scheduler.GetConfigSlug(config.ConfigFilename);

            while (true)
            {
                AnsiConsole.Clear();

                PrintBookings(config.Bookings, null, condeco);

                var addBooking = "Add a new booking";
                var editBooking = "Edit a booking";
                var deleteBooking = "Delete a booking";
                var quit = "Quit";

                var autobookSchedule = Scheduler.GetSchedule("booking", configSlug);
                var checkinSchedule = Scheduler.GetSchedule("checkin", configSlug);

                var scheduledBookingLabel = autobookSchedule != null
                    ? $"Scheduled booking ({autobookSchedule.Summary})"
                    : "Schedule booking (not configured)";

                var scheduledCheckinLabel = checkinSchedule != null
                    ? $"Scheduled check in ({checkinSchedule.Summary})"
                    : "Schedule check in (not configured)";

                var checkForUpdates = "Check for updates";
                var autoUpdateLabel = config.UpdateSettings.AutoUpdate
                    ? "Automatic updates (on)"
                    : "Automatic updates (off)";

                var bookingChoices = new List<string> { addBooking };
                if (config.Bookings.Count > 0)
                {
                    bookingChoices.Add(editBooking);
                    bookingChoices.Add(deleteBooking);
                }

                AnsiConsole.MarkupLine("");
                var prompt = new SelectionPrompt<string>()
                    .Mode(SelectionMode.Leaf)
                    .AddChoiceGroup("Bookings", bookingChoices)
                    .AddChoiceGroup("Scheduling", [scheduledBookingLabel, scheduledCheckinLabel])
                    .AddChoiceGroup("Updates", [checkForUpdates, autoUpdateLabel])
                    .AddChoices([quit]);

                var selectedAction = AnsiConsole.Prompt(prompt);

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

                if (selectedAction == scheduledBookingLabel)
                {
                    HandleScheduleMenu("booking", configSlug, autobookSchedule, condeco);
                }

                if (selectedAction == scheduledCheckinLabel)
                {
                    HandleScheduleMenu("checkin", configSlug, checkinSchedule, condeco);
                }

                if (selectedAction == checkForUpdates)
                {
                    var updated = HandleCheckForUpdates();
                    if (updated)
                    {
                        condeco.LogOut();
                        Environment.Exit(0);
                    }
                }

                if (selectedAction == autoUpdateLabel)
                {
                    config.UpdateSettings.AutoUpdate = !config.UpdateSettings.AutoUpdate;
                    config.Save();
                }

                if (selectedAction == quit)
                {
                    condeco.LogOut();
                    Environment.Exit(0);
                }
            }
        }

        void HandleScheduleMenu(string taskType, string configSlug, ScheduleInfo? existing, ICondeco condeco)
        {
            var friendlyName = taskType == "booking" ? "booking" : "checkin";

            if (existing != null)
            {
                AnsiConsole.MarkupLine($"[yellow]Scheduled {friendlyName}: {existing.Summary}[/]");
                AnsiConsole.MarkupLine("");

                var editStr = "Edit schedule";
                var deleteStr = "Delete schedule";
                var cancelStr = "Cancel";

                var action = AnsiConsole.Prompt(new SelectionPrompt<string>()
                    .AddChoices([editStr, deleteStr, cancelStr]));

                if (action == editStr)
                {
                    try
                    {
                        var (days, time) = PromptForSchedule(taskType, condeco, existing);
                        var exePath = Environment.ProcessPath ?? "condeco-cli";
                        var configPath = Path.GetFullPath(config.ConfigFilename);
                        Scheduler.CreateOrUpdateSchedule(taskType, configSlug, days, time, exePath, configPath, options.API.ToString());
                        AnsiConsole.MarkupLine($"[lime]Schedule updated.[/]");
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[red]Failed to update schedule: {Markup.Escape(ex.Message)}[/]");
                    }
                    WaitForKeypress();
                }
                else if (action == deleteStr)
                {
                    var confirm = AnsiConsole.Confirm($"Delete the scheduled {friendlyName}?", false);
                    if (confirm)
                    {
                        try
                        {
                            Scheduler.DeleteSchedule(taskType, configSlug);
                            AnsiConsole.MarkupLine($"[lime]Schedule deleted.[/]");
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[red]Failed to delete schedule: {Markup.Escape(ex.Message)}[/]");
                        }
                        WaitForKeypress();
                    }
                }
            }
            else
            {
                try
                {
                    var (days, time) = PromptForSchedule(taskType, condeco, null);
                    var exePath = Environment.ProcessPath ?? "condeco-cli";
                    var configPath = Path.GetFullPath(config.ConfigFilename);
                    Scheduler.CreateOrUpdateSchedule(taskType, configSlug, days, time, exePath, configPath, options.API.ToString());
                    AnsiConsole.MarkupLine($"[lime]Schedule created.[/]");
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[red]Failed to create schedule: {Markup.Escape(ex.Message)}[/]");
                }
                WaitForKeypress();
            }
        }

        //Returns true if an update was installed, in which case the program must exit.
        //(The single-file bundle loads assemblies lazily from the exe path, so the process must not continue running after the swap.)
        bool HandleCheckForUpdates(bool pauseOnNoUpdate = true)
        {
            AnsiConsole.MarkupLine("Checking for updates...");

            try
            {
                //The release binaries are large, so allow more than the default 100 second timeout
                using var httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromMinutes(10)
                };

                var (Release, Error) = GitHubRelease.FetchLatest(httpClient);
                var release = Release;
                if (release == null)
                {
                    AnsiConsole.MarkupLine($"[yellow]Could not check for updates: {Markup.Escape(Error ?? "Unknown error")}[/]");
                    if (pauseOnNoUpdate)
                    {
                        WaitForKeypress();
                    }
                    return false;
                }

                var latestVersion = release.GetVersion();
                if (latestVersion == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Could not parse the latest version.[/]");
                    if (pauseOnNoUpdate)
                    {
                        WaitForKeypress();
                    }
                    return false;
                }

                if (!UpdateChecker.IsNewerVersion(Program.PROGRAM_VERSION, latestVersion))
                {
                    AnsiConsole.MarkupLine($"[lime]You are on the latest version ({Program.PROGRAM_VERSION}).[/]");
                    if (pauseOnNoUpdate)
                    {
                        WaitForKeypress();
                    }
                    return false;
                }

                var updater = PlatformUpdater.Create();
                if (updater == null)
                {
                    AnsiConsole.MarkupLine("[yellow]Auto-update is not supported on this platform.[/]");
                    if (pauseOnNoUpdate)
                    {
                        WaitForKeypress();
                    }
                    return false;
                }

                var versionStr = latestVersion.ToString();

                AnsiConsole.MarkupLine($"[lime]A new version is available: {versionStr} (current version: {Program.PROGRAM_VERSION})[/]");

                if (!string.IsNullOrWhiteSpace(release.Body))
                {
                    AnsiConsole.MarkupLine("");
                    AnsiConsole.MarkupLine("[bold]Release notes:[/]");
                    AnsiConsole.MarkupLine(Markup.Escape(release.Body.Trim()));
                    AnsiConsole.MarkupLine("");
                }

                var isBlocked = UpdateChecker.IsVersionBlocked(latestVersion, config.UpdateSettings.FailedVersions);
                if (isBlocked)
                {
                    AnsiConsole.MarkupLine($"[yellow]Version {versionStr} was previously attempted but failed.[/]");
                }

                var confirm = AnsiConsole.Confirm($"Update to {versionStr}?", true);
                if (!confirm)
                {
                    return false;
                }

                AnsiConsole.MarkupLine("Downloading update...");
                var (Outcome, Message) = UpdateChecker.DownloadAndInstall(httpClient, release, updater);

                if (Outcome == UpdateOutcome.Success)
                {
                    if (isBlocked)
                    {
                        config.UpdateSettings.FailedVersions.Remove(versionStr);
                        config.Save();
                    }
                    AnsiConsole.MarkupLine($"[lime]{Markup.Escape(Message)}[/]");
                    AnsiConsole.MarkupLine("condeco-cli will now exit. Please start it again to use the new version.");
                    WaitForKeypress();
                    return true;
                }
                else if (Outcome == UpdateOutcome.Failed)
                {
                    if (!isBlocked)
                    {
                        config.UpdateSettings.FailedVersions.Add(versionStr);
                        config.Save();
                    }
                    AnsiConsole.MarkupLine($"[red]{Markup.Escape(Message)}[/]");
                }
                else
                {
                    AnsiConsole.MarkupLine($"[yellow]{Markup.Escape(Message)}[/]");
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Update check failed: {Markup.Escape(ex.Message)}[/]");
            }

            WaitForKeypress();
            return false;
        }

        static (DayOfWeek[]? Days, TimeOnly Time) PromptForSchedule(string taskType, ICondeco condeco, ScheduleInfo? existing)
        {
            DayOfWeek[]? days;
            TimeOnly defaultTime;

            if (taskType == "booking")
            {
                DayOfWeek suggestedDay;
                if (existing != null)
                {
                    suggestedDay = ParseFirstDay(existing.Days);
                }
                else
                {
                    try
                    {
                        suggestedDay = condeco.GetRolloverDay();
                    }
                    catch
                    {
                        suggestedDay = DayOfWeek.Sunday;
                    }
                }

                defaultTime = existing?.Time ?? new TimeOnly(23, 58);

                var daysOfWeek = Enum.GetValues<DayOfWeek>()
                    .Cast<DayOfWeek>()
                    .OrderBy(d => (d - DayOfWeek.Monday + 7) % 7)
                    .Select(d => d.ToString())
                    .ToList();

                var dayPrompt = new SelectionPrompt<string>()
                    .Title("Run booking on which day?");

                var currentDayStr = suggestedDay.ToString();
                var choices = daysOfWeek.ToList();
                if (choices.Remove(currentDayStr))
                    choices.Insert(0, currentDayStr + " (suggested)");
                dayPrompt.AddChoices(choices);

                var selectedDay = AnsiConsole.Prompt(dayPrompt);
                if (selectedDay.EndsWith(" (suggested)"))
                    selectedDay = selectedDay[..^" (suggested)".Length];

                days = [Enum.Parse<DayOfWeek>(selectedDay)];
            }
            else
            {
                days = null; // daily
                defaultTime = existing?.Time ?? new TimeOnly(8, 0);
            }

            var timeStr = AnsiConsole.Ask("Run at what time? (HH:mm)", defaultTime.ToString("HH:mm"));
            var time = TimeOnly.Parse(timeStr);

            return (days, time);
        }

        static DayOfWeek ParseFirstDay(string daysStr)
        {
            var token = daysStr.Split(',')[0].Trim();
            foreach (DayOfWeek d in Enum.GetValues<DayOfWeek>())
            {
                if (d.ToString().StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    return d;
            }
            return DayOfWeek.Sunday;
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
                                .GetValues<DayOfWeek>()
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
                                            dayStr += OperatingSystem.IsLinux() ? "√" : "✓";
                                        }

                                        if (booking.AutogenName.Equals(highlightBooking)) dayStr = $"[yellow]{dayStr}[/]";

                                        return dayStr;
                                    })
                                    .ToArray();

                    table.AddRow(rowStrings);
                });

            AnsiConsole.Write(table);
        }

        static void WaitForKeypress()
        {
            AnsiConsole.MarkupLine("[grey]Press any key to continue...[/]");
            Console.ReadKey(true);
        }
    }
}
