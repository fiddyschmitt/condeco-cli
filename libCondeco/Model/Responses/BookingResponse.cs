using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class BookingResponse
    {
        public CallResponse CallResponse = new();
        public List<CreatedBooking> CreatedBookings = [];

        public static BookingResponse FromServerResponse(string jsonString)
        {
            var result = JsonConvert.DeserializeObject<BookingResponse>(jsonString)
                            ?? throw new Exception($"Could not deserialize string to BookingResponse:{Environment.NewLine}{jsonString}");

            return result;
        }
    }

    public class CallResponse
    {
        public string ResponseCode = "";
        public string ResponseMessage = "";
    }

    public class CreatedBooking
    {
        public string BookingDate = "";
        public int BookingID;
        public int BookingType;
        public int Status;
    }
}
