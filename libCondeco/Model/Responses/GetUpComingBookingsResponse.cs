using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class GetUpComingBookingsResponse
    {
        public List<UpComingBooking> UpComingBookings = [];

        public static GetUpComingBookingsResponse FromServerResponse(string jsonStr)
        {
            var upcomingBookingsArray = JsonConvert.DeserializeObject<JArray>(jsonStr,
                                            new JsonSerializerSettings
                                            {
                                                DateParseHandling = DateParseHandling.None
                                            });

            var result = new GetUpComingBookingsResponse()
            {
                UpComingBookings = upcomingBookingsArray
                                    ?.Children<JObject>()
                                    .Select(bookingObj =>
                                    {
                                        var bookingJSON = bookingObj.ToString();
                                        var booking = JsonConvert.DeserializeObject<UpComingBooking>(bookingJSON)
                                                        ?? throw new Exception($"Could not deserialize string to {nameof(UpComingBooking)}:{Environment.NewLine}{bookingJSON}");

                                        booking.RawJSON = bookingJSON;

                                        return booking;
                                    })
                                    .ToList() ?? throw new Exception($"Could not deserialize string to {nameof(GetUpComingBookingsResponse)}:{Environment.NewLine}{jsonStr}")
            };

            return result;
        }
    }

    public class UpComingBooking
    {
        public required string BookedLocation;
        public required string Floor;
        public required string BookingTitle;
        public required int BookingStatus;

        public ulong BookingId;
        public ulong BookingItemId;

        public required BookingMetadata BookingMetadata;

        public List<string> OtherSameDayBookings = [];

        public required string RawJSON;
    }

    public class BookingMetadata
    {
        public required Rules Rules;
    }

    public class Rules
    {
        public required bool HdCheckInRequired;
    }
}
