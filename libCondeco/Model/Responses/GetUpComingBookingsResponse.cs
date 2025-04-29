using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class GetUpComingBookingsResponse
    {
        public List<CreatedBooking> CreatedBookings = [];

        public static GetUpComingBookingsResponse FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<GetUpComingBookingsResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(BookingResponse)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class UpComingBooking
    {
        public required string BookingTitle;
        public required int BookedResourceItemId;

        public required string Floor;
        public required string BookedLocation;
        public required int BookingId;
        public required int BookingItemId;
    }
}
