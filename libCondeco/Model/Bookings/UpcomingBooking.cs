using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Bookings
{
    public class UpcomingBooking
    {
        public required int BookingId { get; set; }
        public required int BookingItemId { get; set; }
        public required int LocationId { get; set; }
        public required string BookedLocation { get; set; }
        public required bool CheckInRequired { get; set; }
        public required int DeskId { get; set; }
        public required string BookingTitle { get; set; }
        public required DateTime BookingStartDate { get; set; }
        public required DateTime BookingEndDate { get; set; }
        public required int BookingStatus { get; set; }

        public required bool BookedForSelf { get; set; }
        public required int? BookedForUserId { get; set; }
        public required string? BookedForFullName { get; set; }
    }
}
