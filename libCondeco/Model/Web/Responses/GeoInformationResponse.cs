using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class GeoInformationResponse
    {
        public required object[] RoomTypes { get; set; }
        public required object[] Countries { get; set; }
        public required object[] Regions { get; set; }
        public required object[] Locations { get; set; }
        public required object[] Groups { get; set; }
        public required object[] Floors { get; set; }
        public required Bookingaccess[] BookingAccess { get; set; }
        public required object[] WorkSpaceTypes { get; set; }
        public required object[] RoomSearchableAttributes { get; set; }
        public required Advancedgriduserdetail AdvancedGridUserDetail { get; set; }
        public DateTime CurrentTimeUTC { get; set; }
        public required Globalsetup GlobalSetUp { get; set; }
        public required Screenprofile[] ScreenProfiles { get; set; }
        public int AccessLevel { get; set; }
        public required string VersionID { get; set; }
    }

    public class Advancedgriduserdetail
    {
        public int UserID { get; set; }
        public bool ShowHintGridInfo { get; set; }
        public bool ShowHintGridSetup { get; set; }
        public bool ShowHintAddRooms { get; set; }
    }

    public class Globalsetup
    {
        public int BookingProgressMinutes { get; set; }
    }

    public class Bookingaccess
    {
        public required string Deployment { get; set; }
        public required string AccessName { get; set; }
    }

    public class Screenprofile
    {
        public int ProfileID { get; set; }
        public int CheckInRequired { get; set; }
    }
}
