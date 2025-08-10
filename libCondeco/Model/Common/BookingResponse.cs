using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Common
{
    public class BookingResponse
    {
        public required Callresponse CallResponse { get; set; }
        public required Createdbooking[] CreatedBookings { get; set; }

        public static BookingResponse FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<BookingResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(BookingResponse)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class Createdbooking
    {
        public required string BookingDate { get; set; }
        public int BookingID { get; set; }
        public int BookingType { get; set; }
        public bool QRCodeEnabled { get; set; }
        public int Status { get; set; }
        public bool bookMultipleDesk { get; set; }
    }

}
