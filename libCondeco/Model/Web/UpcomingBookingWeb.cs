using libCondeco.Model.Bookings;
using libCondeco.Model.Web.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web
{
    public class UpcomingBookingWeb : UpcomingBooking
    {
        public required UpComingBooking OriginalBookingObject { get; set; }
    }
}
