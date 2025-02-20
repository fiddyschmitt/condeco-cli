using CommandLine;
using condeco_cli.CLI;
using condeco_cli.Config;
using libCondeco;

namespace condeco_cli
{
    internal class Program
    {
        const string PROGRAM_NAME = "condeco-cli";
        const string PROGRAM_VERSION = "1.0.0";

        static void Main(string[] args)
        {
            Console.WriteLine($"{PROGRAM_NAME} {PROGRAM_VERSION}");
            Console.WriteLine();

            Parser.Default.ParseArguments<AutoBookOptions, AutoCheckinOptions>(args)
                .WithParsed<AutoBookOptions>(RunAutoBook)
                .WithParsed<AutoCheckinOptions>(RunAutoCheckin)
                .WithNotParsed(errors => Console.WriteLine("Invalid command or arguments."));
        }

        static void RunAutoBook(AutoBookOptions opts)
        {
            var config = LoadConfig(opts.Config);

            var condecoWeb = new CondecoWeb(config["Account"]["BaseUrl"]);
            var (LoggedIn, ErrorMessage) = condecoWeb.LogIn(
                                                        config["Account"]["Username"],
                                                        config["Account"]["Password"]);

            if (LoggedIn)
            {
                //condecoWeb.Dump();
                var grid = condecoWeb.GetGrid();

                if (grid == null)
                {
                    Console.WriteLine($"Could not retrieve booking grid. Exiting.");
                    Environment.Exit(1);
                }

                foreach (var section in config.Sections)
                {
                    if (section.SectionName.Equals("Book", StringComparison.OrdinalIgnoreCase))
                    {
                        var country = section["Country"];
                        var location = section["Location"];
                        var group = section["Group"];
                        var floor = section["Floor"];
                        var workspaceType = section["WorkspaceType"];
                        var desk = section["Desk"];

                        var daysToBook = section["Days"]?
                                            .Split(",", StringSplitOptions.TrimEntries)
                                            .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek parsedDay) ? parsedDay : (DayOfWeek?)null)
                                            .Where(day => day.HasValue)
                                            .ToList() ?? [];

                        var rooms = condecoWeb.GetRooms(grid, country, location, group, floor, workspaceType);

                        if (rooms == null)
                        {
                            Console.WriteLine($"Could not retrieve rooms");
                            Environment.Exit(1);
                        }

                        var room = rooms.Rooms.FirstOrDefault(room => room.Name.Equals(desk));

                        if (room == null)
                        {
                            Console.WriteLine($"Could not retrieve room: {desk}");
                            Environment.Exit(1);
                        }

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
                                Console.ForegroundColor = OriginalConsoleColour;
                                Console.Write($"Booking {room.Name} for {date:dd/MM/yyyy}: ");

                                var bookingResponse = condecoWeb.BookRoom(room, date);

                                if (bookingResponse.Success)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"Success");
                                    Console.ForegroundColor = OriginalConsoleColour;
                                }
                                else
                                {
                                    if (bookingResponse.BookingResponse.CallResponse.ResponseCode == "5014")
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"You already have this desk booked.");
                                        Console.ForegroundColor = OriginalConsoleColour;
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{bookingResponse.BookingResponse.CallResponse.ResponseCode}: {bookingResponse.BookingResponse.CallResponse.ResponseMessage}");
                                        Console.ForegroundColor = OriginalConsoleColour;
                                    }
                                }
                            }

                            i++;
                        }

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

        static void RunAutoCheckin(AutoCheckinOptions opts)
        {
            //The server specifies when checkin is possible.
            //    grid.Settings.DeskSettings.CheckInAMTime
            //    grid.Settings.DeskSettings.CheckInPMTime
            Console.WriteLine($"Not implemented.");
        }

        static Ini LoadConfig(string configFilename)
        {
            if (string.IsNullOrEmpty(configFilename) && !File.Exists(DefaultConfigFilename))
            {
                Console.WriteLine($"{DefaultConfigFilename} did not exist. Created example.");
                File.WriteAllText(DefaultConfigFilename, ExampleConfig.ExampleString);
                Console.WriteLine($"Please populate {DefaultConfigFilename} with values.");
                Console.WriteLine("Exiting.");

                Environment.Exit(1);
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
