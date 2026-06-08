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

        public BookingTaskResult Result { get; } = new BookingTaskResult()
        {
            Status = BookingTaskStatus.NotStarted,
            Outcome = string.Empty
        };
        public Booking Booking { get; }
        public Room Room { get; }
        public List<DateOnly> Dates { get; }

        public void StartBooking()
        {
            Result.AttemptsStarted = DateTime.Now;
            Result.Status = BookingTaskStatus.InProgress;

            var bookingDescription = ToString();

            //book task
            Task.Factory.StartNew(() =>
            {
                //one-shot booking
                try
                {
                    Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Started task");

                    var bookingResponseTask = condeco.SendBookingRequest(Room, Dates, Booking.BookFor);

                    Thread.Sleep(5000);

                    if (Result.Status == BookingTaskStatus.InProgress && Dates.Count > 1)
                    {
                        //var rangeBookedSuccessfully = false;
                        //if (bookingResponseTask.IsCompleted)
                        //{
                        //    var response = bookingResponseTask.Result;
                        //    if (response.IsSuccessStatusCode)
                        //    {
                        //        var responseStr = bookingResponseTask.Result.Content.ReadAsStringAsync().Result;
                        //        responseStr = responseStr
                        //                        .Replace("\"", "")
                        //                        .Replace("\\\\n", "");

                        //        if (
                        //            int.TryParse(responseStr, out _) ||     //web
                        //            responseStr.Contains("\"ResponseCode\":100")    //mobile
                        //            )
                        //        {
                        //            rangeBookedSuccessfully = true;
                        //            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Range booking successful: {responseStr}");
                        //        }
                        //    }
                        //}

                        //if (!rangeBookedSuccessfully)
                        {
                            //Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Range booking not successful. Booking individual days.");

                            Dates
                                .Select(date => new BookingTask(condeco, Booking, Room, [date], maxDuration))
                                .ToList()
                                .ForEach(subbooking => subbooking.StartBooking());
                        }
                    }

                    bookingResponseTask.Wait();

                    if (bookingResponseTask.IsCompleted)
                    {
                        var responseStr = bookingResponseTask.Result.Content.ReadAsStringAsync().Result;
                        Result.Outcome = responseStr;
                        Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Result: {responseStr}");
                    }
                }
                catch (Exception ex)
                {
                    var innermost = ex;
                    while (innermost is AggregateException agg && agg.InnerException != null)
                        innermost = agg.InnerException;

                    Result.Outcome = innermost.Message;
                    Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Error while sending booking request: {innermost}");
                }
            }, TaskCreationOptions.LongRunning);
        }

        public void StartChecking()
        {
            var stopAt = DateTime.Now.Add(maxDuration);

            var bookingDescription = ToString();

            //check task
            Task.Factory.StartNew(() =>
            {
                var confirmedDates = new HashSet<DateOnly>();

                while (Result.Status == BookingTaskStatus.InProgress)
                {
                    try
                    {
                        foreach (var date in Dates)
                        {
                            if (!confirmedDates.Contains(date) && condeco.BookingSuccessful(Room, date, Booking.BookFor))
                            {
                                confirmedDates.Add(date);
                            }
                        }
                        if (confirmedDates.Count == Dates.Count)
                        {
                            Result.AttemptsFinished = DateTime.Now;
                            Result.Status = BookingTaskStatus.BookingSuccessful;
                        }
                        else
                        {
                            var timedOut = DateTime.Now > stopAt;
                            if (timedOut)
                            {
                                Result.AttemptsFinished = DateTime.Now;
                                Result.Status = BookingTaskStatus.BookingTimedOut;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Error while checking booking: {ex.Message}");

                        if (ex.Message.Contains("401"))
                        {
                            Console.WriteLine($"{DateTime.Now}  [{bookingDescription}]  Session expired. Stopping confirmation checks.");
                            Result.AttemptsFinished = DateTime.Now;
                            Result.Status = BookingTaskStatus.BookingTimedOut;
                            break;
                        }
                    }

                    Thread.Sleep(10_000);
                }
            }, TaskCreationOptions.LongRunning);
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
        public required BookingTaskStatus Status;
        public required string Outcome;

        public DateTime AttemptsStarted;
        public DateTime AttemptsFinished;
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
