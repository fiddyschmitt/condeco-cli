using CommandLine;
using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Extensions;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.Common;
using libCondeco.Model.Space;
using Spectre.Console;

namespace condeco_cli
{
    internal class Program
    {
        const string PROGRAM_NAME = "condeco-cli";
        const string PROGRAM_VERSION = "1.6.0";

        static void Main(string[] args)
        {
            Console.WriteLine($"{PROGRAM_NAME} {PROGRAM_VERSION}");
            Console.WriteLine($"Current date: {DateTime.Now:yyyy-MM-dd HHmm ss}");
            Console.WriteLine();

            if (args.Length == 0 ||
                (args.Length == 2 && (args.ToList().Contains("--config") || args.ToList().Contains("--api"))) ||
                (args.Length == 4 && (args.ToList().Contains("--config") && args.ToList().Contains("--api"))))
            {
                Parser.Default.ParseArguments<BaseOptions>(args)
                .WithParsed(static opts =>
                {
                    if (!AnsiConsole.Profile.Capabilities.Interactive)
                    {
                        AnsiConsole.MarkupLine("[red]Environment does not support interaction.[/]");
                        return;
                    }

                    LoadConfig(opts.Config, true);

                    if (config == null)
                    {
                        AnsiConsole.MarkupLine("Error: config file not loaded.");
                        Environment.Exit(0);
                    }

                    var interactionSession = new InteractiveSession(opts, config);
                    interactionSession.Run();
                });

            }
            else
            {
                Parser.Default
                    .ParseArguments<AutoBookOptions, CheckInOptions, DumpOptions>(args)
                    .WithParsed<AutoBookOptions>(RunAutoBook)
                    .WithParsed<CheckInOptions>(RunCheckIn)
                    .WithParsed<DumpOptions>(RunDump)
                    .WithNotParsed(errors => Console.WriteLine("Invalid command or arguments."));
            }
        }

        static void NonInteractiveLogin(ICondeco condeco)
        {
            if (config == null)
            {
                Console.WriteLine($"{nameof(NonInteractiveLogin)} - cannot log in. Config is null.");
                Environment.Exit(1);
            }

            Console.WriteLine($"Logging into {condeco.BaseUrl}");

            var loggedIn = false;
            var errorMessage = "";

            if (!string.IsNullOrEmpty(config.Account.Username))
            {
                (loggedIn, errorMessage) = condeco.LogIn(config.Account.Username, config.Account.Password);
            }
            else if (!string.IsNullOrEmpty(config.Account.Token))
            {
                (loggedIn, errorMessage) = condeco.LogIn(config.Account.Token);
            }

            if (loggedIn)
            {
                Console.WriteLine($"Login successful.");
            }
            else
            {
                Console.WriteLine($"Login unsuccessful.");
                Console.WriteLine(errorMessage);
                Console.WriteLine("Terminating.");
                Environment.Exit(1);
            }

        }

        private static readonly Random random = new();

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

            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(0);
            }

            var condeco = BuildCondecoInterface(opts, config);
            NonInteractiveLogin(condeco);



            var startBookingFrom = condeco.GetBookingWindowStartDate();
            var bookUntil = condeco.GetBookingWindowEndDate();

            if (opts.WaitForRolloverMinutes != null && waitForRollover)
            {
                //Let's wait for the new booking window
                Console.WriteLine($"Will wait a total of {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes.Value)} for the new booking window.");

                var originalStartDate = condeco.GetBookingWindowStartDate();
                var originalEndDate = condeco.GetBookingWindowEndDate();

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

                    var bookingWindowStartDate = condeco.GetBookingWindowStartDate();
                    var bookingWindowEndDate = condeco.GetBookingWindowEndDate();

                    if (originalEndDate != bookingWindowEndDate)
                    {
                        Console.WriteLine($"The new booking window is now available.");
                        Console.WriteLine($"It changed from [{originalStartDate} - {originalEndDate}] to [{bookingWindowStartDate} - {bookingWindowEndDate}]");
                        Console.WriteLine($"Will now proceed with booking.");

                        startBookingFrom = originalEndDate.AddDays(1);
                        bookUntil = bookingWindowEndDate;
                        break;
                    }

                    Thread.Sleep(1000);
                }
            }

            foreach (var booking in config.Bookings)
            {
                var daysToBook = booking
                                    .Days
                                    .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek parsedDay) ? parsedDay : (DayOfWeek?)null)
                                    .Where(day => day.HasValue)
                                    .ToList() ?? [];

                List<Room> rooms = [];
                try
                {
                    rooms = condeco.GetRooms(
                                        booking.Country,
                                        booking.Location,
                                        booking.Group,
                                        booking.Floor,
                                        booking.WorkspaceType);
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

                var room = rooms.FirstOrDefault(room => room.Name.Equals(booking.Desk));

                if (room == null)
                {
                    Console.WriteLine($"Room not found: {booking.Desk}");
                    Console.WriteLine();

                    Console.WriteLine($"Valid room:");
                    Console.WriteLine($"{string.Join(Environment.NewLine, rooms.Select(item => $"\t{item.Name}").OrderBy(item => item))}");
                    Environment.Exit(1);
                }

                var datesToBook = new List<DateOnly>();

                var i = 0;
                while (true)
                {
                    var date = DateOnly.FromDateTime(startBookingFrom.Date.AddDays(i));
                    if (date.ToDateTime(TimeOnly.MinValue) > bookUntil)
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
                            (bool Success, BookingResponse BookingResponse)? bookingResult = null;
                            Exception? exception = null;
                            var attempt = 0;

                            for (var i = 0; i < 5; i++)
                            {
                                attempt++;

                                try
                                {
                                    bookingResult = condeco.BookRoom(room, date, booking.BookFor);

                                    if (bookingResult.HasValue && bookingResult.Value.Success)
                                    {
                                        exception = null;
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    exception = ex;

                                    var toSleepSeconds = attempt * 10;
                                    toSleepSeconds = toSleepSeconds + random.Next(0, toSleepSeconds);
                                    Thread.Sleep(toSleepSeconds * 1000);
                                }
                            }

                            return new
                            {
                                Date = date,
                                BookingResult = bookingResult,
                                Exception = exception,
                                Attempts = attempt
                            };
                        }, Math.Min(16, datesToBook.Count))
                        .Select(res =>
                        {
                            Console.ForegroundColor = OriginalConsoleColour;

                            if (booking.BookFor == null)
                            {
                                Console.Write($"Booking {room.Name} for {res.Date:dd/MM/yyyy}");
                            }
                            else
                            {
                                Console.Write($"Booking {room.Name} for {booking.BookFor.FirstName} {booking.BookFor.LastName} on {res.Date:dd/MM/yyyy}");
                            }

                            if (res.Attempts == 1)
                            {
                                Console.Write($": ");
                            }
                            else
                            {
                                Console.Write($" after {res.Attempts} attempts: ");
                            }


                            if (res.Exception == null)
                            {
                                if (res.BookingResult != null)
                                {
                                    if (res.BookingResult.Value.Success)
                                    {
                                        Console.ForegroundColor = ConsoleColor.Green;
                                        Console.WriteLine($"Success");
                                        Console.ForegroundColor = OriginalConsoleColour;
                                    }
                                    else
                                    {
                                        Console.ForegroundColor = ConsoleColor.Yellow;
                                        Console.WriteLine($"{res.BookingResult.Value.BookingResponse.CallResponse.ResponseCode}: {res.BookingResult.Value.BookingResponse.CallResponse.ResponseMessage}");
                                        Console.ForegroundColor = OriginalConsoleColour;
                                    }
                                }
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.Red;
                                Console.Write($"Failed.");
                                Console.ForegroundColor = OriginalConsoleColour;
                                Console.WriteLine($" {res.Exception}");
                            }

                            return "";
                        })
                        .ToList();


                Console.WriteLine();
            }

            condeco.LogOut();
        }

        static void RunCheckIn(CheckInOptions opts)
        {
            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(0);
            }

            var condeco = BuildCondecoInterface(opts, config);
            NonInteractiveLogin(condeco);

            var today = DateOnly.FromDateTime(DateTime.Now);
            var upcomingBookings = condeco.GetUpcomingBookings(today);

            var checkinsToPerform = upcomingBookings
                                    .Where(booking => booking.BookingStartDate.Date == DateTime.Now.Date)   //the mobile API can only check in for today
                                    .Where(booking => booking.CheckInRequired)
                                    .ToList();


            if (checkinsToPerform.Count == 0)
            {
                Console.WriteLine($"There are no check-ins to perform at this time.");
            }
            else
            {
                checkinsToPerform
                    .ForEach(upcomingBooking =>
                    {
                        Console.ForegroundColor = OriginalConsoleColour;
                        Console.Write($"Checking in to {upcomingBooking.BookingTitle} at {upcomingBooking.BookedLocation}");

                        if (!upcomingBooking.BookedForSelf && !string.IsNullOrEmpty(upcomingBooking.BookedForFullName))
                        {
                            Console.Write($" for {upcomingBooking.BookedForFullName}");
                        }

                        if (upcomingBooking.BookingId != 0)
                        {
                            Console.Write($" (booking {upcomingBooking.BookingId}, {upcomingBooking.BookingItemId})");
                        }
                        Console.Write($": ");

                        (var checkinSuccessful, var bookingStatusStr) = condeco.CheckIn(upcomingBooking);

                        if (checkinSuccessful)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"Success");
                            Console.ForegroundColor = OriginalConsoleColour;
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine($"Unsuccessful ({bookingStatusStr})");
                            Console.ForegroundColor = OriginalConsoleColour;
                        }
                    });
            }

            condeco.LogOut();
        }

        static void RunDump(DumpOptions opts)
        {
            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(0);
            }

            var condecoWeb = new CondecoWeb(config.Account.BaseUrl);
            NonInteractiveLogin(condecoWeb);

            condecoWeb.Dump();
        }

        static CondecoCliConfig? config;

        static void LoadConfig(string providedConfigFilename, bool interative)
        {
            var configFilename = providedConfigFilename;

            if (string.IsNullOrEmpty(configFilename) && !File.Exists(DefaultConfigFilename))
            {
                if (!interative)
                {
                    Console.WriteLine($"Config not found.");

                    Console.WriteLine($"Please run condeco-cli without arguments to populate the config.");
                    Console.WriteLine("Exiting.");
                    Environment.Exit(1);
                }
            }

            if (string.IsNullOrEmpty(configFilename))
            {
                configFilename = DefaultConfigFilename;
            }

            config = new CondecoCliConfig(configFilename);
        }

        static void CheckAccountIsPopulated(CondecoCliConfig? config)
        {
            if (config == null || (string.IsNullOrEmpty(config.Account.Username) && string.IsNullOrEmpty(config.Account.Token)))
            {
                Console.WriteLine($"Please run condeco-cli without arguments to populate the config.");
                Console.WriteLine("Exiting.");

                Environment.Exit(1);
            }
        }

        public static ICondeco BuildCondecoInterface(BaseOptions options, CondecoCliConfig condecoCliConfig)
        {
            ICondeco? result = null;
            if (options.API == EnumAPI.web)
            {
                result = new CondecoWeb(condecoCliConfig.Account.BaseUrl);
            }

            if (options.API == EnumAPI.mobile)
            {
                result = new CondecoMobile(condecoCliConfig.Account.BaseUrl);
            }

            if (result == null) throw new Exception($"API not supported: {options.API}");
            return result;
        }

        public static readonly ConsoleColor OriginalConsoleColour = Console.ForegroundColor;
        const string DefaultConfigFilename = "config.ini";
    }
}
