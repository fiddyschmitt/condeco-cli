using condeco_cli.Model;
using libCondeco;
using libCondeco.Model.Space;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace condeco_cli.Bookings
{
    public class BookingTask
    {
        public BookingTask(ICondeco condeco, Booking booking, Room room, List<DateOnly> dates, TimeSpan maxDuration)
        {
            this.condeco = condeco;
            Booking = booking;
            Room = room;
            Dates = dates;
            this.maxDuration = maxDuration;
        }

        private readonly ICondeco condeco;
        private readonly TimeSpan maxDuration;

        //Last write-path error surfaced by SendAndDiscard, used to suppress consecutive repeats.
        //Written from concurrent write tasks; a lost update only means an occasional duplicate log line.
        private string? lastLoggedWriteError;

        //Dates a write response says the server already holds a booking for. Only used to stop the
        //speculative volleys re-firing at a day one of them has already landed - it is deliberately NOT
        //used by the booking loop, which keeps re-submitting until a confirmation READ succeeds, because
        //a write response claiming the booking exists has since been shown to be survivable-but-untrue
        //(3-Aug-2026: confirmed bookings were replaced by different ids minutes later).
        private readonly HashSet<DateOnly> writeReportedAsBooked = [];

        public bool WriteReportedAsBooked(DateOnly date)
        {
            lock (writeReportedAsBooked)
            {
                return writeReportedAsBooked.Contains(date);
            }
        }

        //Records what a booking write said about a date, so concurrent volleys don't pile duplicate
        //writes onto a day that is already booked.
        public void NoteWriteResponse(DateOnly date, string responseBody)
        {
            if (!IndicatesBookingExists(responseBody))
            {
                return;
            }

            lock (writeReportedAsBooked)
            {
                writeReportedAsBooked.Add(date);
            }
        }

        //True when a SaveDeskBooking response says the booking is now in place: a bare booking id, or one
        //of the server's "you already hold this" refusals. Deliberately excludes horizon rejections, an
        //empty body, a 500 page, and "this space was just booked" (which means somebody else took it).
        private static bool IndicatesBookingExists(string responseBody)
        {
            var cleaned = (responseBody ?? string.Empty)
                            .Replace("\"", "")
                            .Replace("\\n", "")
                            .Trim();

            if (cleaned.Length == 0)
            {
                return false;
            }

            return int.TryParse(cleaned, out _)
                    || cleaned.Contains("already reserved", StringComparison.OrdinalIgnoreCase)
                    || cleaned.Contains("already have personal space bookings", StringComparison.OrdinalIgnoreCase);
        }

        public BookingTaskResult Result { get; } = new BookingTaskResult()
        {
            Status = BookingTaskStatus.NotStarted
        };
        public Booking Booking { get; }
        public Room Room { get; }
        public List<DateOnly> Dates { get; }

        //Guards every status transition. The status is written from three places - StartBooking, the
        //booking loop, and the window watcher's prune - and on 10-Aug-2026 the prune lost the race:
        //it marked a beyond-horizon task NotRequired one moment before StartBooking (whose caller had
        //just seen the task as NotStarted) stomped it back to InProgress, resurrecting a task nothing
        //would ever stop again.
        private readonly object statusLock = new();

        //Atomically moves Status from one specific state to another. Returns false, changing nothing,
        //if the status is no longer what the caller saw.
        private bool TryTransition(BookingTaskStatus from, BookingTaskStatus to)
        {
            lock (statusLock)
            {
                if (Result.Status != from)
                {
                    return false;
                }

                Result.Status = to;
                return true;
            }
        }

        //Marks this task as not required (e.g. its dates fall outside the real booking window). Safe to
        //call at any point: a task that hasn't started will never start, a running task's loop observes
        //the change and stops, and a task that already reached a terminal outcome is left untouched.
        public void MarkNotRequired()
        {
            lock (statusLock)
            {
                if (Result.Status == BookingTaskStatus.NotStarted || Result.Status == BookingTaskStatus.InProgress)
                {
                    Result.Status = BookingTaskStatus.NotRequired;
                }
            }
        }

        public void StartBooking()
        {
            //Only ever start from NotStarted - in particular, never resurrect a task the window watcher
            //has pruned to NotRequired since the caller checked.
            if (!TryTransition(BookingTaskStatus.NotStarted, BookingTaskStatus.InProgress))
            {
                return;
            }

            Result.AttemptsStarted = DateTime.Now;

            var bookingDescription = ToString();

            //Per-request timeout for booking writes AND confirmation reads: a tenth of the overall
            //window. Everything else (login, grid) keeps libCondeco's global 15-minute timeout. At the
            //midnight storm these requests routinely hang for minutes; bounding them lets us re-submit and
            //re-check quickly instead of blocking on one stuck request. Floored so a tiny
            //--wait-for-rollover doesn't produce an absurdly twitchy timeout.
            var requestTimeout = maxDuration / 10;
            if (requestTimeout < TimeSpan.FromSeconds(30))
            {
                requestTimeout = TimeSpan.FromSeconds(30);
            }

            var stopAt = DateTime.Now.Add(maxDuration);

            //A booking that reads back as confirmed can still disappear minutes later: on 3-Aug-2026 three
            //of Georgia's five days were confirmed at 00:00:13 and had been replaced by different booking
            //ids by the time the run was inspected. So don't finish the moment everything confirms - hold,
            //re-read, and only then declare success. Kept well inside the overall window.
            var reverifyDelay = TimeSpan.FromSeconds(90);
            if (reverifyDelay > maxDuration / 3)
            {
                reverifyDelay = maxDuration / 3;
            }

            Task.Factory.StartNew(() =>
            {
                Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Started task");

                var confirmedDates = new HashSet<DateOnly>();
                DateTime? allConfirmedSince = null;

                //Each round: (re)submit every still-unconfirmed day, discard the responses, then read back
                //the confirmations. The response body is unreliable at the rollover (500 page / empty / id /
                //"already reserved"), so the confirmation read - not the response - is the only source of
                //truth. Re-submitting is deliberate: a 500'd write is sometimes silently dropped
                //server-side, and trying again is the only thing that reliably lands the booking.
                while (Result.Status == BookingTaskStatus.InProgress)
                {
                    var reverifiedClean = false;

                    try
                    {
                        SubmitWrites(confirmedDates, requestTimeout, bookingDescription);
                        CollectConfirmations(confirmedDates, requestTimeout);

                        //Everything has been confirmed for long enough - read every day once more before
                        //declaring success, in case the server has since dropped any of them.
                        if (allConfirmedSince != null && DateTime.Now - allConfirmedSince >= reverifyDelay)
                        {
                            var stillBooked = new HashSet<DateOnly>();
                            CollectConfirmations(stillBooked, requestTimeout);

                            var lost = Dates.Where(date => !stillBooked.Contains(date)).ToList();
                            if (lost.Count > 0)
                            {
                                var lostStr = string.Join(", ", lost.Select(date => date.ToString("dd/MM/yyyy")));
                                Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  {lost.Count} confirmed day(s) disappeared after being booked ({lostStr}). Re-booking.");
                            }

                            confirmedDates.Clear();
                            foreach (var date in stillBooked)
                            {
                                confirmedDates.Add(date);
                            }

                            reverifiedClean = lost.Count == 0;
                            allConfirmedSince = null;
                        }
                    }
                    catch (Exception ex)
                    {
                        var innermost = Unwrap(ex);

                        if (SessionExpiredException.IsSessionExpired(innermost))
                        {
                            //A 401 is normally re-authenticated transparently by the ReAuthHandler;
                            //reaching here means that automatic re-login attempt failed. That can be
                            //transient (login.aspx itself 500s during the rollover storm), and the booking
                            //writes ride the long-lived cookie rather than the expired bearer - so keep
                            //submitting. The next 401 triggers a fresh re-login attempt.
                            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Session expired and automatic re-login failed. Continuing to submit; re-login will be retried.");
                        }
                        else
                        {
                            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Error during booking loop: {innermost.Message}");
                        }
                    }

                    //Publish progress even when the round faulted - CollectConfirmations harvests
                    //completed checks before surfacing a fault, so confirmedDates can have grown.
                    Result.RoundsAttempted++;
                    Result.ConfirmedDates = confirmedDates.ToList();

                    var allConfirmed = confirmedDates.Count == Dates.Count;

                    //Success is only declared once a re-read, taken reverifyDelay after everything first
                    //confirmed, still finds every day booked. Terminal states are set via TryTransition so
                    //a prune that landed mid-round stays NotRequired.
                    if (reverifiedClean)
                    {
                        Result.AttemptsFinished = DateTime.Now;
                        TryTransition(BookingTaskStatus.InProgress, BookingTaskStatus.BookingSuccessful);
                        break;
                    }

                    if (allConfirmed && allConfirmedSince == null)
                    {
                        allConfirmedSince = DateTime.Now;
                        Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  All {Dates.Count} day(s) confirmed. Re-checking in {reverifyDelay.TotalSeconds:N0}s before finishing.");
                    }
                    else if (!allConfirmed)
                    {
                        allConfirmedSince = null;
                    }

                    if (DateTime.Now > stopAt)
                    {
                        Result.AttemptsFinished = DateTime.Now;

                        //Everything was booked, we just ran out of window before the re-read. Report what
                        //we know rather than downgrading a fully-confirmed booking to a timeout.
                        TryTransition(BookingTaskStatus.InProgress, allConfirmed
                                                                        ? BookingTaskStatus.BookingSuccessful
                                                                        : BookingTaskStatus.BookingTimedOut);
                        break;
                    }

                    //A short sleep is enough pacing: each round already blocks on the server's responses
                    //(bounded by requestTimeout), so a struggling server slows the loop down naturally,
                    //and the rollover is a race decided in the opening seconds (on 6-Jul-2026 a rival bot
                    //took a contested day within ~6 booking ids of ours).
                    var sleepDuration = TimeSpan.FromSeconds(5);

                    //While waiting out the re-check there is nothing to submit or read, so wait it out in
                    //one go rather than spinning through no-op rounds.
                    if (allConfirmedSince != null)
                    {
                        var remainingHold = reverifyDelay - (DateTime.Now - allConfirmedSince.Value);
                        if (remainingHold > sleepDuration)
                        {
                            sleepDuration = remainingHold;
                        }
                    }

                    //Never sleep past the window: the run must still get a chance to report.
                    var remainingWindow = stopAt - DateTime.Now;
                    if (sleepDuration > remainingWindow)
                    {
                        sleepDuration = remainingWindow;
                    }

                    if (sleepDuration > TimeSpan.Zero)
                    {
                        Thread.Sleep(sleepDuration);
                    }
                }
            }, TaskCreationOptions.LongRunning);
        }

        //Submits a booking write for every still-unconfirmed day and discards every response. Days are
        //always requested individually, never as a range: the server treats a range as all-or-nothing, so
        //a rival holding a single day refuses the whole range and we'd win nothing (observed 6-Jul-2026,
        //where 20+ range re-submits were refused while the four uncontested days went unbooked for 22
        //minutes). All writes in a round are fired concurrently and each is bounded by requestTimeout.
        //Already-confirmed days are never re-submitted.
        private void SubmitWrites(HashSet<DateOnly> confirmedDates, TimeSpan requestTimeout, string bookingDescription)
        {
            var batches = Dates
                            .Where(date => !confirmedDates.Contains(date))
                            .Select(date => new List<DateOnly> { date })
                            .ToList();

            if (batches.Count == 0)
            {
                return;
            }

            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Submitting {batches.Count} unconfirmed day(s) individually.");

            var timeoutSources = batches.Select(_ => new CancellationTokenSource(requestTimeout)).ToList();
            try
            {
                var writes = batches
                                .Select((dates, index) => SendAndDiscard(dates, timeoutSources[index].Token))
                                .ToArray();
                Task.WaitAll(writes);
            }
            finally
            {
                foreach (var timeoutSource in timeoutSources)
                {
                    timeoutSource.Dispose();
                }
            }
        }

        //Fires a single booking write and throws the response away. A timeout (cancellation), a 500, or a
        //dropped connection are all swallowed: confirmation is read separately, and we simply re-submit on
        //the next round.
        private async Task SendAndDiscard(List<DateOnly> dates, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await condeco.SendBookingRequest(Room, dates, Booking.BookFor, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException)
            {
                //Hit requestTimeout - routine during the rollover storm, and the write may still land
                //server-side; the wire log has the details.
            }
            catch (Exception ex)
            {
                //Still discarded, but surfaced: a persistent failure (proxy/DNS/not-logged-in) would
                //otherwise make every write fail silently for the whole window. Consecutive repeats of
                //the same message are suppressed to avoid a line per day per round.
                var message = Unwrap(ex).Message;
                if (message != lastLoggedWriteError)
                {
                    lastLoggedWriteError = message;
                    Console.WriteLine($"{DateTime.Now}  [{ToString()}]  Error while sending booking request (repeats suppressed): {message}");
                }
            }
        }

        //Reads back which still-unconfirmed days are now booked and adds them to confirmedDates. Each read
        //is fired concurrently and bounded by requestTimeout; transient read failures (timeouts, 500s)
        //during the storm are treated as not-yet-confirmed and retried next round. A genuine session
        //expiry (automatic re-login failed) is propagated so the main loop can stop.
        private void CollectConfirmations(HashSet<DateOnly> confirmedDates, TimeSpan requestTimeout)
        {
            var datesToCheck = Dates.Where(date => !confirmedDates.Contains(date)).ToList();
            if (datesToCheck.Count == 0)
            {
                return;
            }

            var timeoutSources = datesToCheck.Select(_ => new CancellationTokenSource(requestTimeout)).ToList();
            try
            {
                var checks = datesToCheck
                                .Select((date, index) => Task.Run(() =>
                                {
                                    try
                                    {
                                        var confirmed = condeco.BookingSuccessful(Room, date, Booking.BookFor, timeoutSources[index].Token);
                                        return (date, confirmed);
                                    }
                                    catch (Exception ex) when (!SessionExpiredException.IsSessionExpired(Unwrap(ex)))
                                    {
                                        //Transient read failure during the storm (timeout / 500). Treat as
                                        //not-yet-confirmed; we'll re-check next round.
                                        return (date, confirmed: false);
                                    }
                                }))
                                .ToArray();

                try
                {
                    Task.WaitAll(checks);
                }
                catch
                {
                    //Swallowed so the harvest below still runs: without it, a session expiry on one
                    //date would discard the confirmations the other dates returned in the same round.
                    //The fault is re-surfaced after the harvest.
                }

                foreach (var check in checks)
                {
                    if (check.IsCompletedSuccessfully && check.Result.confirmed)
                    {
                        confirmedDates.Add(check.Result.date);
                    }
                }

                //The per-date lambdas swallow everything except genuine session expiry, so a faulted
                //check can only be a session expiry; surface one so the main loop can log it.
                var sessionExpiry = checks
                                        .Where(check => check.IsFaulted)
                                        .Select(check => Unwrap(check.Exception!))
                                        .FirstOrDefault();
                if (sessionExpiry != null)
                {
                    throw sessionExpiry;
                }
            }
            finally
            {
                foreach (var timeoutSource in timeoutSources)
                {
                    timeoutSource.Dispose();
                }
            }
        }

        private static Exception Unwrap(Exception ex)
        {
            var innermost = ex;
            while (innermost is AggregateException aggregate && aggregate.InnerException != null)
            {
                innermost = aggregate.InnerException;
            }

            return innermost;
        }

        public override string ToString()
        {
            var fromDate = Dates.First();
            var toDate = Dates.Last();

            var bookingFor = Booking.GetBookingForFullName(condeco);

            var bookingDescription = $"Book {Room.Name} for {bookingFor} on {fromDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy}";

            return bookingDescription;
        }
    }

    public class BookingTaskResult
    {
        //volatile: written under statusLock in BookingTask, but read locklessly by the booking loop and
        //by Program's monitor loop on other threads. volatile makes those reads see the latest value
        //without relying on incidental barriers.
        public required volatile BookingTaskStatus Status;

        public DateTime AttemptsStarted;
        public DateTime AttemptsFinished;

        //Progress published by the booking loop for reporting. ConfirmedDates is the set of days read
        //back as booked; RoundsAttempted is how many submit-then-check rounds completed.
        public int RoundsAttempted;
        public List<DateOnly> ConfirmedDates = [];
    }

    public enum BookingTaskStatus
    {
        NotStarted,
        InProgress,
        BookingSuccessful,
        BookingTimedOut,
        NotRequired
    }
}
