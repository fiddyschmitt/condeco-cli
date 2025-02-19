using CommandLine;
using condeco_cli.CLI;
using IniParser;
using IniParser.Model;
using libCondeco;
using System.Security.Policy;

namespace condeco_cli
{
    internal class Program
    {
        const string PROGRAM_NAME = "condeco-cli";
        const string PROGRAM_VERSION = "1.0.0";

        static void Main(string[] args)
        {
            Console.WriteLine($"{PROGRAM_NAME} {PROGRAM_VERSION}");

            Parser.Default.ParseArguments<AutoBookOptions, AutoCheckinOptions>(args)
                .WithParsed<AutoBookOptions>(opts => RunAutoBook(opts))
                .WithParsed<AutoCheckinOptions>(opts => RunAutoCheckin(opts))
                .WithNotParsed(errors => Console.WriteLine("Invalid command or arguments."));
        }

        static void RunAutoBook(AutoBookOptions opts)
        {
            var config = LoadConfig(opts.Config);

            var condecoWeb = new CondecoWeb(config["Account"]["BaseUrl"]);
            var (Success, ErrorMessage) = condecoWeb.LogIn(
                                                        config["Account"]["Username"],
                                                        config["Account"]["Password"]);

            if (Success)
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
                        var country = section.Keys["Country"];
                        var location = section.Keys["Location"];
                        var group = section.Keys["Group"];
                        var floor = section.Keys["Floor"];
                        var workspaceType = section.Keys["WorkspaceType"];
                        var desk = section.Keys["Desk"];

                        var daysToBook = section
                                    .Keys["Days"]
                                    .Split(",", StringSplitOptions.TrimEntries)
                                    .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek parsedDay) ? parsedDay : (DayOfWeek?)null)
                                    .Where(day => day.HasValue)
                                    .ToList();

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
                                Console.Write($"Booking {date}: ");

                                var bookingResponse = condecoWeb.BookRoom(room, date);

                                if (bookingResponse.Success)
                                {
                                    Console.ForegroundColor = ConsoleColor.Green;
                                    Console.WriteLine($"Success");
                                    Console.ForegroundColor = OriginalConsoleColour;
                                }
                                else
                                {
                                    //if (bookingResponse.BookingResponse.CallResponse.ResponseCode == "5014")

                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"{bookingResponse.BookingResponse.CallResponse.ResponseCode}: {bookingResponse.BookingResponse.CallResponse.ResponseMessage}");
                                    Console.ForegroundColor = OriginalConsoleColour;
                                }
                            }

                            i++;
                        }
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
            Console.WriteLine($"Running AutoCheckin with config: {opts.Config}");
        }

        static IniData LoadConfig(string configFilename)
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

            var parser = new FileIniDataParser();
            parser.Parser.Configuration.AllowDuplicateSections = true;
            parser.Parser.Configuration.AllowDuplicateKeys = true;
            var data = parser.ReadFile(configFilename);

            if (string.IsNullOrEmpty(data["Account"]["Username"]))
            {
                Console.WriteLine($"Please populate {DefaultConfigFilename} with values.");
                Console.WriteLine("Exiting.");

                Environment.Exit(1);
            }

            return data;
        }

        public static readonly ConsoleColor OriginalConsoleColour = Console.ForegroundColor;
        const string DefaultConfigFilename = "config.ini";
    }
}
