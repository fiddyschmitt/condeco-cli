using CommandLine;
using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Extensions;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.Responses;

namespace condeco_cli
{
    internal class Program
    {
        const string PROGRAM_NAME = "condeco-cli";
        const string PROGRAM_VERSION = "1.3.0";

        static void Main(string[] args)
        {
            Console.WriteLine($"{PROGRAM_NAME} {PROGRAM_VERSION}");
            Console.WriteLine();

            if (args.Length == 0 && !File.Exists(DefaultConfigFilename))
            {
                CreateDefaultConfigFile();
                Environment.Exit(0);
            }

            Parser.Default
                .ParseArguments<AutoBookOptions, DumpOptions>(args)
                .WithParsed<AutoBookOptions>(RunAutoBook)
                .WithParsed<DumpOptions>(RunDump)
                .WithNotParsed(errors => Console.WriteLine("Invalid command or arguments."));
        }

        static void RunAutoBook(AutoBookOptions opts)
        {
            var waitForRollover = false;
            if (opts.WaitForRolloverMinutes != null)
            {
                if (opts.WaitForRolloverMinutes.Value < 1)
                {
                    Console.WriteLine($"--wait-for-rollover must be between 1 and 5 minutes inclusive.");
                    Environment.Exit(1);
                }

                if (opts.WaitForRolloverMinutes.Value > 5)
                {
                    Console.WriteLine($"--wait-for-rollover must not exceed 5 minutes.");
                    Environment.Exit(1);
                }

                waitForRollover = true;
            }

            var config = LoadConfig(opts.Config);

            var condecoWeb = new CondecoWeb(config["Account"]["BaseUrl"]);
            var (LoggedIn, ErrorMessage) = condecoWeb.LogIn(
                                                        config["Account"]["Username"],
                                                        config["Account"]["Password"]);

            if (LoggedIn)
            {
                foreach (var section in config.Sections)
                {
                    if (section.SectionName.Equals("Book", StringComparison.OrdinalIgnoreCase))
                    {
                        var country = section["Country"];
                        var location = section["Location"];
                        var group = section["Group"];
                        var floor = section["Floor"];
                        var workspaceTypeName = section["WorkspaceType"];
                        var desk = section["Desk"];

                        var daysToBook = section["Days"]?
                                            .Split(",", StringSplitOptions.TrimEntries)
                                            .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek parsedDay) ? parsedDay : (DayOfWeek?)null)
                                            .Where(day => day.HasValue)
                                            .ToList() ?? [];

                        var grid = condecoWeb.GetGrid(workspaceTypeName);

                        if (grid == null)
                        {
                            Console.WriteLine($"Could not retrieve booking grid. Exiting.");
                            Environment.Exit(1);
                        }

                        RoomsResponse? rooms = null;
                        try
                        {
                            rooms = condecoWeb.GetRooms(grid, country, location, group, floor, workspaceTypeName);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                            Environment.Exit(1);
                        }

                        if (rooms == null)
                        {
                            Console.WriteLine($"Could not retrieve rooms");
                            Environment.Exit(1);
                        }

                        var room = rooms.Rooms.FirstOrDefault(room => room.Name.Equals(desk));

                        if (room == null)
                        {
                            Console.WriteLine($"Room not found: {desk}");
                            Console.WriteLine();

                            Console.WriteLine($"Valid room:");
                            Console.WriteLine($"{string.Join(Environment.NewLine, rooms.Rooms.Select(item => $"\t{item.Name}").OrderBy(item => item))}");
                            Environment.Exit(1);
                        }

                        if (opts.WaitForRolloverMinutes != null && waitForRollover)
                        {
                            //Let's wait for the new booking window
                            Console.WriteLine($"Will wait a total of {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes.Value)} for the new booking window.");

                            var originalStartDate = grid.Settings.DeskSettings.StartDate;
                            var originalEndDate = grid.Settings.DeskSettings.EndDate;
                            var pollCount = 0;
                            var stopPollingAt = DateTime.Now.AddMinutes(opts.WaitForRolloverMinutes.Value);
                            while (true)
                            {
                                if (DateTime.Now > stopPollingAt)
                                {
                                    Console.WriteLine($"Waited for {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes.Value)} but the new booking window is still not available. Exiting.");
                                    Environment.Exit(1);
                                }

                                Console.WriteLine($"Checking for rollover - attempt {++pollCount:N0}");
                                grid = condecoWeb.GetGrid(workspaceTypeName);

                                if (grid == null)
                                {
                                    Console.WriteLine($"Could not retrieve booking grid. Exiting.");
                                    Environment.Exit(1);
                                }

                                if (originalEndDate != grid.Settings.DeskSettings.EndDate)
                                {
                                    Console.WriteLine($"The new booking window is now available.");
                                    Console.WriteLine($"It changed from [{originalStartDate} - {originalEndDate}] to [{grid.Settings.DeskSettings.StartDate} - {grid.Settings.DeskSettings.EndDate}]");
                                    Console.WriteLine($"Will now proceed with booking.");
                                    waitForRollover = false;
                                    break;
                                }

                                Thread.Sleep(1000);
                            }
                        }

                        var datesToBook = new List<DateOnly>();

                        var i = 0;
                        while (true)
                        {
                            var date = DateOnly.FromDateTime(DateTime.Now.Date.AddDays(i));
                            if (date.ToDateTime(TimeOnly.MinValue) > grid.Settings.DeskSettings.EndDate)
                            {
                                break;
                            }

                            if (daysToBook.Contains(date.DayOfWeek))
                            {
                                datesToBook.Add(date);
                            }

                            i++;
                        }

                        _ = datesToBook
                                .SelectParallelPreserveOrder(date =>
                                {
                                    var bookingResult = condecoWeb.BookRoom(room, date);

                                    return new
                                    {
                                        Date = date,
                                        BookingResult = bookingResult
                                    };
                                }, Math.Min(16, datesToBook.Count))
                                .Select(res =>
                                {
                                    Console.ForegroundColor = OriginalConsoleColour;
                                    Console.Write($"Booking {room.Name} for {res.Date:dd/MM/yyyy}: ");

                                    if (res.BookingResult.Success)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"Success");
                                        Console.ForegroundColor = OriginalConsoleColour;
                                    }
                                    else
                                    {
                                        if (res.BookingResult.BookingResponse.CallResponse.ResponseCode == "5014")
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"You already have this desk booked.");
                                            Console.ForegroundColor = OriginalConsoleColour;
                                        }
                                        else
                                        {
                                            Console.ForegroundColor = ConsoleColor.Yellow;
                                            Console.WriteLine($"{res.BookingResult.BookingResponse.CallResponse.ResponseCode}: {res.BookingResult.BookingResponse.CallResponse.ResponseMessage}");
                                            Console.ForegroundColor = OriginalConsoleColour;
                                        }
                                    }

                                    return "";
                                })
                                .ToList();


                        Console.WriteLine();
                    }
                }

                condecoWeb.LogOut();
            }
            else
            {
                Console.WriteLine($"Login unsuccessful.");
                Console.WriteLine(ErrorMessage);
                Console.WriteLine("Terminating.");
                Environment.Exit(1);
            }
        }

        static void RunDump(DumpOptions opts)
        {
            var config = LoadConfig(opts.Config);

            var condecoWeb = new CondecoWeb(config["Account"]["BaseUrl"]);
            var (LoggedIn, ErrorMessage) = condecoWeb.LogIn(
                                                        config["Account"]["Username"],
                                                        config["Account"]["Password"]);

            if (LoggedIn)
            {
                condecoWeb.Dump();
            }
            else
            {
                Console.WriteLine($"Login unsuccessful.");
                Console.WriteLine(ErrorMessage);
                Console.WriteLine("Terminating.");
                Environment.Exit(1);
            }
        }

        static void CreateDefaultConfigFile()
        {
            if (!File.Exists(DefaultConfigFilename))
            {
                Console.WriteLine($"Created default config file: {DefaultConfigFilename}");
                File.WriteAllText(DefaultConfigFilename, ExampleConfig.ExampleString);
                Console.WriteLine($"Please populate {DefaultConfigFilename} with values.");
                Console.WriteLine("Exiting.");
                Environment.Exit(1);
            }
        }

        static Ini LoadConfig(string configFilename)
        {
            if (string.IsNullOrEmpty(configFilename) && !File.Exists(DefaultConfigFilename))
            {
                CreateDefaultConfigFile();
            }

            if (string.IsNullOrEmpty(configFilename))
            {
                configFilename = DefaultConfigFilename;
            }

            var ini = new Ini();
            ini.Parse(File.ReadAllText(configFilename));

            if (string.IsNullOrEmpty(ini["Account"]["Username"]))
            {
                Console.WriteLine($"Please populate {DefaultConfigFilename} with values.");
                Console.WriteLine("Exiting.");

                Environment.Exit(1);
            }

            return ini;
        }

        public static readonly ConsoleColor OriginalConsoleColour = Console.ForegroundColor;
        const string DefaultConfigFilename = "config.ini";
    }
}
