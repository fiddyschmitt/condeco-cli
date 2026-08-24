using CommandLine;
using condeco_cli.Bookings;
using condeco_cli.CLI;
using condeco_cli.Config;
using condeco_cli.Extensions;
using condeco_cli.Updating;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.Common;
using libCondeco.Model.Space;
using libCondeco.Web;
using Spectre.Console;

namespace condeco_cli
{
    internal class Program
    {
        const string PROGRAM_NAME = "condeco-cli";
        internal const string PROGRAM_VERSION = "1.7.0";

        //FPS 15/11/2025: The condeco API can be called at most 50 times per second, otherwise it returns "API calls quota exceeded! maximum admitted 50 per Second."
        private static IHttpClientFactory httpClientFactory = null!;

        static void Main(string[] args)
        {
            Console.WriteLine($"{PROGRAM_NAME} {PROGRAM_VERSION}");
            Console.WriteLine($"Current date: {DateTime.Now:yyyy-MM-dd HHmm ss}");
            Console.WriteLine();

            var exePath = Environment.ProcessPath;
            var exeDir = Path.GetDirectoryName(exePath);
            var updater = PlatformUpdater.Create();
            if (exePath != null && exeDir != null && updater != null)
            {
                //Take the update lock so we don't delete the .update file of a concurrent in-progress update
                var lockPath = Path.Combine(exeDir, UpdateLock.LockFileName);
                using var updateLock = UpdateLock.TryAcquire(lockPath);
                if (updateLock != null)
                {
                    updater.CleanupOldFiles(exePath);
                }
            }

            Parser.Default.ParseArguments<BaseOptions>(args)
                .WithParsed(opts =>
                {
                    httpClientFactory = new RateLimitedHttpClientFactory(opts.Verbose);

                    if (opts.WaitForRolloverMinutes != null && !opts.AutoBook)
                    {
                        Console.WriteLine("--wait-for-rollover can only be used with --autobook.");
                        Environment.Exit(1);
                    }

                    if (opts.AutoBook)
                        RunAutoBook(opts);
                    else if (opts.CheckIn)
                        RunCheckIn(opts);
                    else if (opts.Dump)
                        RunDump(opts);
                    else
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

                        var interactionSession = new InteractiveSession(opts, config, httpClientFactory);
                        interactionSession.Run();
                    }
                });
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
            else if (config.Account.IsSsoAccount)
            {
                //SSO/Eptura One: the only durable credential is the refresh token. Mint a fresh session
                //from it (for platform tenants this also runs the platform-token exchange internally).
                var mobileRefresh = condeco as CondecoMobile;
                if (string.IsNullOrEmpty(config.Account.RefreshToken) || mobileRefresh == null)
                {
                    errorMessage = "No refresh token available to refresh the SSO session. Please re-authenticate interactively.";
                }
                else
                {
                    Console.WriteLine("[SSO] Refreshing the session from the stored refresh token...");
                    var ssoConfig = mobileRefresh.DetectSso();
                    if (ssoConfig == null)
                    {
                        errorMessage = "Could not determine the SSO configuration. Please re-authenticate interactively.";
                    }
                    else
                    {
                        var refreshResult = mobileRefresh.RefreshSsoToken(ssoConfig, config.Account.RefreshToken);
                        loggedIn = refreshResult.Success;
                        errorMessage = refreshResult.ErrorMessage;

                        if (loggedIn && refreshResult.Tokens != null)
                        {
                            config.Account.RefreshToken = refreshResult.Tokens.RefreshToken ?? config.Account.RefreshToken;
                            config.Save();
                            Console.WriteLine("[SSO] Refreshed tokens saved to config.");
                        }
                        else if (!loggedIn)
                        {
                            errorMessage = "Token expired and refresh failed. Please re-authenticate interactively.";
                        }
                    }
                }
            }
            else
            {
                errorMessage = "No credentials configured. Please run condeco-cli interactively to log in.";
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

        static void RunAutoBook(BaseOptions opts)
        {
            var testingRollover = false;

            var waitForRollover = false;
            if (opts.WaitForRolloverMinutes != null)
            {
                if (opts.WaitForRolloverMinutes.Value < 1)
                {
                    Console.WriteLine($"--wait-for-rollover must be between 1 and {BaseOptions.MAX_WAIT_DURATION_MINUTES} minutes inclusive.");
                    Environment.Exit(1);
                }

                if (opts.WaitForRolloverMinutes.Value > BaseOptions.MAX_WAIT_DURATION_MINUTES)
                {
                    Console.WriteLine($"--wait-for-rollover must not exceed {BaseOptions.MAX_WAIT_DURATION_MINUTES} minutes.");
                    Environment.Exit(1);
                }

                waitForRollover = true;
            }

            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(1);
            }

            var condeco = BuildCondecoInterface(opts, config);
            NonInteractiveLogin(condeco);



            //Tell the API which groups we're going to book in before asking for the booking window: the
            //advance-booking period is a per-group setting, and the mobile API can only infer the window
            //from it.
            condeco.SetBookingScope(config.Bookings.Select(booking => (booking.Location, booking.Group)));

            var startBookingFrom = condeco.GetBookingWindowStartDate();
            var bookUntil = condeco.GetBookingWindowEndDate();

            var originalStartDate = startBookingFrom;
            var originalEndDate = bookUntil;

            if (opts.WaitForRolloverMinutes != null && waitForRollover)
            {
                //for now, let's assume the booking window will be the same size. We'll trim it down when the new booking window opens
                var bookingWindowSize = bookUntil - startBookingFrom;
                startBookingFrom = bookUntil.AddDays(1);
                bookUntil = startBookingFrom.Add(bookingWindowSize);
            }

            if (testingRollover)
            {
                startBookingFrom = condeco.GetBookingWindowStartDate();
                bookUntil = condeco.GetBookingWindowEndDate();
            }

            //prepare the booking tasks
            var bookingGroups = config
                                .Bookings
                                .Select(booking =>
                                {
                                    //retrieve the room details
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


                                    //determine the days to book
                                    var daysToBook = booking
                                                        .Days
                                                        .Select(day => Enum.TryParse(day.Trim(), true, out DayOfWeek parsedDay) ? parsedDay : (DayOfWeek?)null)
                                                        .Where(day => day.HasValue)
                                                        .ToList() ?? [];

                                    var datesToBook = new List<DateOnly>();
                                    var i = 0;
                                    while (true)
                                    {
                                        var date = DateOnly.FromDateTime(startBookingFrom.Date.AddDays(i));
                                        if (date.ToDateTime(TimeOnly.MinValue) > bookUntil)
                                        {
                                            break;
                                        }

                                        var exclude = booking
                                                        .ExcludeDates
                                                        .Exists(exclude => date >= exclude.FromDate && date <= exclude.ToDate);

                                        if (!exclude && daysToBook.Contains(date.DayOfWeek))
                                        {
                                            datesToBook.Add(date);
                                        }

                                        i++;
                                    }

                                    TimeSpan maxDuration;
                                    if (opts.WaitForRolloverMinutes.HasValue)
                                    {
                                        maxDuration = TimeSpan.FromMinutes(opts.WaitForRolloverMinutes.Value);
                                    }
                                    else
                                    {
                                        maxDuration = TimeSpan.FromMinutes(BaseOptions.MAX_WAIT_DURATION_MINUTES);
                                    }

                                    var ranges = datesToBook
                                                         .GroupAdjacent((prev, cur) => cur <= prev.AddDays(1))
                                                         .ToList();

                                    var rangeBookings = ranges
                                                            .Select(range =>
                                                            {
                                                                var bookingTask = new BookingTask(condeco, booking, room, range.ToList(), maxDuration);

                                                                var bookingDescription = bookingTask.ToString();
                                                                Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Prepared task");

                                                                return bookingTask;
                                                            })
                                                            .ToList();

                                    return rangeBookings;
                                })
                                .ToList();

            DateTime? stopAt = null;

            var waitTasks = new List<Task>();

            if (opts.WaitForRolloverMinutes != null && waitForRollover)
            {
                stopAt = DateTime.Now.AddMinutes(opts.WaitForRolloverMinutes.Value);

                var waitForNewBookingWindow = Task.Factory.StartNew(() =>
                {
                    //Let's wait for the new booking window
                    Console.WriteLine($"{DateTime.Now}  Will wait a total of {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes.Value)} for the new booking window.");

                    while (true)
                    {
                        try
                        {
                            if (DateTime.Now > stopAt)
                            {
                                Console.WriteLine($"{DateTime.Now}  Waited for {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes.Value)} but the new booking window is still not available. Exiting.");
                                Environment.Exit(1);
                            }

                            //Console.WriteLine($"{DateTime.Now}  Checking for rollover - attempt {++pollCount:N0}");

                            var (StartDate, EndDate) = condeco.GetBookingWindow();
                            var bookingWindowStartDate = StartDate;
                            var bookingWindowEndDate = EndDate;

                            //Only an end date that moved FORWARD is the real new window. Under the rollover
                            //storm the server can return degraded desk settings with missing or nonsense
                            //dates (13-Jul-2026: the window "shrank" from [13/7 - 24/7] to [13/7 - 13/7],
                            //which pruned every task and ended the run with nothing booked). Anything that
                            //is not an expansion is treated as a bad read and polling continues.
                            if (bookingWindowEndDate > originalEndDate)
                            {
                                Console.WriteLine($"{DateTime.Now}  The new booking window is now available.");
                                Console.WriteLine($"{DateTime.Now}  It changed from [{originalStartDate} - {originalEndDate}] to [{bookingWindowStartDate} - {bookingWindowEndDate}]");

                                //now that the actual booking window is known, let's trim the booking tasks
                                var notRequired = bookingGroups
                                                    .SelectMany(bookingGroup => bookingGroup)
                                                    .Where(bookingTask => bookingTask.Dates.First().ToDateTime(TimeOnly.MinValue) < bookingWindowStartDate || bookingTask.Dates.First().ToDateTime(TimeOnly.MinValue) > bookingWindowEndDate)
                                                    .ToList();

                                Console.WriteLine($"{DateTime.Now}  Pruning {notRequired.Count:N0} tasks which are not required.");
                                notRequired
                                    .ForEach(bookingTask => bookingTask.MarkNotRequired());

                                var totalBookings = bookingGroups
                                                        .Sum(grp => grp.Count);

                                Console.WriteLine($"{DateTime.Now}  {totalBookings - notRequired.Count:N0} tasks remain.");

                                break;
                            }
                            else if (bookingWindowEndDate != originalEndDate)
                            {
                                Console.WriteLine($"{DateTime.Now}  Ignoring implausible booking window [{bookingWindowStartDate} - {bookingWindowEndDate}] - the end date did not move forward, so this is likely a degraded server response.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"{DateTime.Now}  Error while waiting for new booking window details: {ex}.");

                            if (SessionExpiredException.IsSessionExpired(ex))
                            {
                                Console.WriteLine($"{DateTime.Now}  Session expired. Stopping window detection.");
                                break;
                            }
                        }

                        Thread.Sleep(5000);
                    }
                }, TaskCreationOptions.LongRunning);

                var waitForHourRollover = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        var beforeCall = DateTime.UtcNow;
                        var currentServerTime = condeco.GetServerDateTimeUTC();
                        var afterCall = DateTime.UtcNow;
                        var roundTrip = afterCall - beforeCall;
                        var clockDelta = afterCall - currentServerTime;
                        Console.WriteLine($"{DateTime.Now}  Server time: {currentServerTime:O}  Local UTC: {afterCall:O}  API call took: {roundTrip.TotalSeconds:N2}s  Delta (local - server): {clockDelta.TotalSeconds:N2}s");

                        var nextHour = new DateTime(currentServerTime.Year, currentServerTime.Month, currentServerTime.Day, currentServerTime.Hour, 0, 0).AddHours(1);

                        if (testingRollover)
                        {
                            nextHour = new DateTime(currentServerTime.Year, currentServerTime.Month, currentServerTime.Day, currentServerTime.Hour, currentServerTime.Minute, currentServerTime.Second).AddSeconds(15);
                        }

                        var durationToRollover = nextHour - currentServerTime;
                        var earliestSleep = durationToRollover - roundTrip;
                        var latestSleep = durationToRollover;

                        Console.WriteLine($"{DateTime.Now}  Rollover window: earliest in {earliestSleep.TotalSeconds:N2}s, latest in {latestSleep.TotalSeconds:N2}s (uncertainty: {(latestSleep - earliestSleep).TotalSeconds:N2}s)");

                        //Every task races, including single-day ones. (An older filter limited this to
                        //multi-day tasks, a leftover from when the speculative fire was a range request.)
                        var speculativeBookingTasks = bookingGroups
                            .SelectMany(bg => bg)
                            .ToList();

                        if (earliestSleep > TimeSpan.Zero)
                            Thread.Sleep(earliestSleep);

                        var speculativeDayCount = speculativeBookingTasks.Sum(bt => bt.Dates.Count);
                        Console.WriteLine($"{DateTime.Now}  Earliest rollover reached. Firing {speculativeDayCount:N0} speculative day {"booking".Pluralize(speculativeDayCount)} per volley.");

                        //Three rapid fire-and-forget volleys, one second apart, on their own task so they
                        //neither block this waiter (StartBooking must begin at the calculated rollover
                        //moment) nor wait for responses (a hanging server can't delay the next volley).
                        //The rollover is a race decided in the opening seconds (on 6-Jul-2026 a rival bot
                        //took a contested day within ~6 booking ids of ours), and the volley spread also
                        //covers the clock-uncertainty window between the earliest and latest possible
                        //rollover. BookingTask's confirm-and-repair loop takes over moments later.
                        Task.Factory.StartNew(() =>
                        {
                            try
                            {
                                const int SPECULATIVE_VOLLEYS = 3;
                                for (var volley = 1; volley <= SPECULATIVE_VOLLEYS; volley++)
                                {
                                    Console.WriteLine($"{DateTime.Now}  Firing speculative volley {volley} of {SPECULATIVE_VOLLEYS}.");
                                    foreach (var bt in speculativeBookingTasks)
                                    {
                                        //No longer sent as a range: the server treats a range as
                                        //all-or-nothing, so a rival grabbing a single day refuses the whole
                                        //range (observed 6-Jul-2026). Individual days mean a contested day
                                        //costs only that day.
                                        //condeco.SendBookingRequest(bt.Room, bt.Dates, bt.Booking.BookFor, tag: "speculative");

                                        foreach (var date in bt.Dates)
                                        {
                                            //An earlier volley already landed this day, so don't pile a
                                            //duplicate concurrent write onto it. Racing writes for the same
                                            //day are the leading suspect behind bookings that confirm and
                                            //then vanish (3-Aug-2026).
                                            if (bt.WriteReportedAsBooked(date))
                                            {
                                                continue;
                                            }

                                            _ = SendSpeculativeBooking(condeco, bt, date);
                                        }
                                    }

                                    if (volley < SPECULATIVE_VOLLEYS)
                                    {
                                        Thread.Sleep(1_000);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"{DateTime.Now}  Error while firing speculative volleys: {ex.Message}.");
                            }
                        }, TaskCreationOptions.LongRunning);

                        var remainingSleep = latestSleep - earliestSleep;
                        if (remainingSleep > TimeSpan.Zero)
                            Thread.Sleep(remainingSleep);

                        Console.WriteLine($"{DateTime.Now}  Hour has rolled over.");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{DateTime.Now}  Error while calculating duration until hour rolls over: {ex.Message}.");
                    }
                }, TaskCreationOptions.LongRunning);

                //wait for the new booking window OR the hour to roll over
                waitTasks.Add(waitForNewBookingWindow);
                waitTasks.Add(waitForHourRollover);
            }

            if (waitTasks.Count > 0)
            {
                Task.WhenAny(waitTasks).Wait();
            }

            Console.WriteLine($"{DateTime.Now}  Will now proceed with booking.");

            var bookingTasks = bookingGroups
                                .SelectMany(bookingGroup => bookingGroup)
                                .ToList();

            //start the booking tasks
            bookingTasks
                .Where(bookingTask => bookingTask.Result.Status == BookingTaskStatus.NotStarted)
                .ToList()
                .ForEach(bookingTask => bookingTask.StartBooking());

            //monitor tasks until completion
            foreach (var bookingTask in bookingTasks)
            {
                try
                {
                    var bookingDescription = bookingTask.ToString();

                    while (bookingTask.Result.Status == BookingTaskStatus.InProgress)
                    {
                        if (stopAt.HasValue && DateTime.Now > stopAt)
                        {
                            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Waited for {opts.WaitForRolloverMinutes} {"minute".Pluralize(opts.WaitForRolloverMinutes ?? 1)} but booking is still in progress. Exiting.");

                            //This cap can fire a couple of minutes before the tasks' own timeouts (it
                            //starts ticking when the wait begins, ~23:58, not at the rollover), and this is
                            //the run's last output - so report EVERY task, not just this one: others may
                            //have already succeeded or hold partial confirmations.
                            foreach (var task in bookingTasks)
                            {
                                ReportFinalStatus(task);
                            }

                            TryLogOut(condeco);
                            Environment.Exit(1);
                        }

                        Thread.Sleep(100);
                    }

                    var res = bookingTask.Result;

                    if (res.Status == BookingTaskStatus.BookingSuccessful ||
                        res.Status == BookingTaskStatus.BookingTimedOut)
                    {
                        var duration = res.AttemptsFinished - res.AttemptsStarted;

                        Console.ForegroundColor = OriginalConsoleColour;
                        Console.Write($"[{res.AttemptsStarted.TimeOfDay:hh\\:mm\\:ss} - {res.AttemptsFinished.TimeOfDay:hh\\:mm\\:ss}]  [{duration.TotalSeconds:N0} seconds]  {bookingDescription}: ");

                        switch (res.Status)
                        {
                            case BookingTaskStatus.BookingSuccessful:
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.WriteLine($"Success");
                                Console.ForegroundColor = OriginalConsoleColour;
                                break;

                            case BookingTaskStatus.BookingTimedOut:
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine(DescribeUnconfirmed(bookingTask));
                                Console.ForegroundColor = OriginalConsoleColour;
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"{DateTime.Now}  Error while monitoring task completion: {ex.Message}.");
                }
            }

            //Every booking task has reached a terminal state (the monitor loop above only exits a task's
            //wait on Success/TimedOut/NotRequired). This line is the clean-finish marker: unlike the
            //wait-cap and error exits, reaching here means we ran the whole process to completion, so its
            //presence in booking.log is the signal that the run wasn't killed or timed out mid-flight.
            Console.WriteLine($"{DateTime.Now}  Booking process complete. Exiting.");

            TryLogOut(condeco);

            TryAutoUpdate(config);
        }

        //A booking task that ends without full confirmation isn't necessarily a failure. At the midnight
        //rollover the server routinely 500s the write while still creating the booking, and the read path
        //can lag the write by tens of minutes, so "unconfirmed" usually means "probably booked, just not
        //verifiable in time". Report what we actually know - how many days we confirmed and how hard we
        //tried - instead of a bare "Timed out".
        static string DescribeUnconfirmed(BookingTask task)
        {
            var res = task.Result;

            //Snapshot the confirmed-days list once: the booking loop reassigns Result.ConfirmedDates to a
            //fresh list every round, and this can run (via the wait-cap path) while a task is still in
            //progress. Reading the reference twice could otherwise mix a count from one round with a
            //membership test from another and print a self-contradictory line.
            var confirmedDates = res.ConfirmedDates;

            //AttemptsFinished is still unset when this is called for a task that's in progress (the
            //monitor cap fired before the task's own timeout).
            var finishedAt = res.AttemptsFinished == default ? DateTime.Now : res.AttemptsFinished;
            var duration = finishedAt - res.AttemptsStarted;
            var withinTime = $"not verified within {duration.TotalSeconds:N0}s";

            var totalDays = task.Dates.Count;
            var confirmedDays = confirmedDates.Count;
            var roundsWord = "round".Pluralize(res.RoundsAttempted);

            if (confirmedDays == 0)
            {
                return $"Unconfirmed — 0/{totalDays} day(s) confirmed after {res.RoundsAttempted} submit {roundsWord}; {withinTime}. The booking may still have landed";
            }

            var unconfirmedDates = task.Dates
                                        .Where(date => !confirmedDates.Contains(date))
                                        .Select(date => date.ToString("dd/MM/yyyy"))
                                        .ToList();
            var unconfirmedStr = string.Join(", ", unconfirmedDates);

            return $"Partially confirmed — {confirmedDays}/{totalDays} day(s) confirmed after {res.RoundsAttempted} submit {roundsWord}; {unconfirmedStr} still unconfirmed ({withinTime}, may still have landed)";
        }

        //One line summarising where a booking task ended up. Used when the run is cut short and the
        //normal per-task completion reporting won't get a chance to run.
        static void ReportFinalStatus(BookingTask task)
        {
            var description = task.ToString();

            switch (task.Result.Status)
            {
                case BookingTaskStatus.BookingSuccessful:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"{description}: Success");
                    break;

                case BookingTaskStatus.NotRequired:
                    Console.WriteLine($"{description}: Not required");
                    break;

                case BookingTaskStatus.NotStarted:
                    Console.WriteLine($"{description}: Not started");
                    break;

                default:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"{description}: {DescribeUnconfirmed(task)}");
                    break;
            }

            Console.ForegroundColor = OriginalConsoleColour;
        }

        //Fires one best-effort speculative booking write and discards the response. Bounded by its own
        //timeout and disposed, so hung requests can't pin pooled connections during the seconds that
        //decide the rollover race - BookingTask's loop, which starts moments later, is the reliable path
        //that re-submits every unconfirmed day.
        static async Task SendSpeculativeBooking(ICondeco condeco, BookingTask bookingTask, DateOnly date)
        {
            using var timeoutSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));

            try
            {
                using var response = await condeco.SendBookingRequest(bookingTask.Room, [date], bookingTask.Booking.BookFor, tag: "speculative", cancellationToken: timeoutSource.Token);

                //Tell the task what the server said, so the remaining volleys skip a day this one booked.
                var responseBody = await response.Content.ReadAsStringAsync(timeoutSource.Token);
                bookingTask.NoteWriteResponse(date, responseBody);
            }
            catch
            {
                //Best-effort by design.
            }
        }

        //A logout that times out (e.g. the server is still overloaded at the rollover) must never crash a
        //run that has already done its booking, nor skip the post-run update check. Log it and carry on.
        static void TryLogOut(ICondeco condeco)
        {
            try
            {
                condeco.LogOut();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now}  Error during logout (ignored): {ex.Message}");
            }
        }

        static void RunCheckIn(BaseOptions opts)
        {
            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(1);
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

                        string bookingFor;
                        if (upcomingBooking.BookedForSelf)
                        {
                            bookingFor = condeco.GetFullName();
                        }
                        else
                        {
                            bookingFor = upcomingBooking.BookedForFullName ?? "Unknown";
                        }

                        Console.Write($" for {bookingFor}");

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

            TryLogOut(condeco);

            TryAutoUpdate(config);
        }

        static void RunDump(BaseOptions opts)
        {
            LoadConfig(opts.Config, false);
            CheckAccountIsPopulated(config);

            if (config == null)
            {
                AnsiConsole.MarkupLine("Error: config file not loaded.");
                Environment.Exit(1);
            }

            var condecoWeb = new CondecoWeb(httpClientFactory, config.Account.BaseUrl);
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
            if (config == null || !config.Account.IsConfigured)
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
                result = new CondecoWeb(httpClientFactory, condecoCliConfig.Account.BaseUrl);
            }

            if (options.API == EnumAPI.mobile)
            {
                var mobile = new CondecoMobile(httpClientFactory, condecoCliConfig.Account.BaseUrl);

                if (!string.IsNullOrEmpty(condecoCliConfig.Account.Username))
                {
                    //The mobile API cannot report the real booking window (its advance-period settings
                    //are decorative, not enforced), so pair it with a web session that reads the true
                    //window from the grid. Only possible for username/password accounts - SSO/token
                    //accounts stay pure mobile because the web API cannot log in with a token.
                    var webForWindow = new CondecoWeb(httpClientFactory, condecoCliConfig.Account.BaseUrl);
                    result = new CondecoHybrid(mobile, webForWindow);
                }
                else
                {
                    result = mobile;
                }
            }

            if (result == null) throw new Exception($"API not supported: {options.API}");
            return result;
        }

        static void TryAutoUpdate(CondecoCliConfig? config)
        {
            if (config == null || !config.UpdateSettings.AutoUpdate)
            {
                return;
            }

            try
            {
                var updater = PlatformUpdater.Create();
                if (updater == null)
                {
                    Console.WriteLine($"{DateTime.Now}  Auto-update is not supported on this platform.");
                    return;
                }

                Console.WriteLine($"{DateTime.Now}  Checking for updates...");

                //The release binaries are large, so allow more than the default 100 second timeout
                using var httpClient = new HttpClient()
                {
                    Timeout = TimeSpan.FromMinutes(10)
                };

                var fetchResult = GitHubRelease.FetchLatest(httpClient);
                var release = fetchResult.Release;
                if (release == null)
                {
                    Console.WriteLine($"{DateTime.Now}  Could not check for updates: {fetchResult.Error}");
                    return;
                }

                var latestVersion = release.GetVersion();
                if (latestVersion == null)
                {
                    Console.WriteLine($"{DateTime.Now}  Could not parse a version from the release tag '{release.TagName}'.");
                    return;
                }

                if (!UpdateChecker.IsNewerVersion(PROGRAM_VERSION, latestVersion))
                {
                    Console.WriteLine($"{DateTime.Now}  Already on the latest version ({PROGRAM_VERSION}).");
                    return;
                }

                Console.WriteLine($"{DateTime.Now}  A new version is available: {latestVersion} (current version: {PROGRAM_VERSION}).");

                if (UpdateChecker.IsVersionBlocked(latestVersion, config.UpdateSettings.FailedVersions))
                {
                    Console.WriteLine($"{DateTime.Now}  Skipping version {latestVersion} because a previous update attempt failed.");
                    return;
                }

                Console.WriteLine($"{DateTime.Now}  Downloading and installing version {latestVersion}...");
                var (Outcome, Message) = UpdateChecker.DownloadAndInstall(httpClient, release, updater);
                Console.WriteLine($"{DateTime.Now}  {Message}");

                if (Outcome == UpdateOutcome.Failed)
                {
                    Console.WriteLine($"{DateTime.Now}  Recording version {latestVersion} as failed, so it isn't attempted again.");
                    config.UpdateSettings.FailedVersions.Add(latestVersion.ToString());
                    config.Save();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{DateTime.Now}  Auto-update check failed: {ex.Message}");
            }
        }

        public static readonly ConsoleColor OriginalConsoleColour = Console.ForegroundColor;
        const string DefaultConfigFilename = "config.ini";
    }
}
