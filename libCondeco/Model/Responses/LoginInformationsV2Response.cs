using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{

    public class LoginInformationsV2Response
    {
        public Roomresults RoomResults { get; set; }
        public Deskresults DeskResults { get; set; }
        public DateTime NextRefreshTimeUTC { get; set; }
        public bool IntuneEnabled { get; set; }
        public int UserID { get; set; }
        public int AccessLevel { get; set; }
        public bool PendoEnabled { get; set; }
        public bool MandateBookingToSetInOffice { get; set; }
        public int TeamDayMaxAllowedBookings { get; set; }
        public int TeamMaxAllowedMembers { get; set; }
        public bool TeamDayEnabled { get; set; }
        public int IntelligentBookingTeams { get; set; }
        public string UserPin { get; set; }
        public string UserEmail { get; set; }
        public int AIBookingEnabledForUser { get; set; }
        public bool QRCodeEnabled { get; set; }
        public bool AddToOutlookVisible { get; set; }
        public bool AddToOutlookChecked { get; set; }
        public bool SCSubscriptionStatus { get; set; }
        public bool EnableVisitor { get; set; }
        public Visitortype[] VisitorType { get; set; }
        public bool EnableConcierge { get; set; }
        public object[] ConciergeLocationList { get; set; }
        public int EpturaAI { get; set; }

        public static LoginInformationsV2Response FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<LoginInformationsV2Response>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(LoginInformationsV2Response)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class Roomresults
    {
        public bool Success { get; set; }
        public string SessionGUID { get; set; }
        public Systemsettings SystemSettings { get; set; }
        public Error Error { get; set; }
    }

    public class Systemsettings
    {
        public bool RoomBookingAllowed { get; set; }
    }

    public class Error
    {
        public int ErrorCode { get; set; }
        public string ErrorDescription { get; set; }
    }

    public class Deskresults
    {
        public string AccessToken { get; set; }
        public Masterdata[] MasterData { get; set; }
        public Defaultsettings DefaultSettings { get; set; }
        public Systemsettings1 SystemSettings { get; set; }
        public object[] Bookings { get; set; }
        public string UserFirstName { get; set; }
        public string UserLastName { get; set; }
        public string UName { get; set; }
        public object[] SearchableAttributes { get; set; }
        public Searchableattributesv2[] SearchableAttributesV2 { get; set; }
        public Profilesection[] ProfileSections { get; set; }
        public object[] Roles { get; set; }
        public object[] SearchableAttributesHPS { get; set; }
        public Callresponse CallResponse { get; set; }
    }

    public class Defaultsettings
    {
        public int DefaultCountry { get; set; }
        public string DefaultFloor { get; set; }
        public int DefaultGroup { get; set; }
        public int DefaultLocation { get; set; }
        public int DefaultRegion { get; set; }
    }

    public class Systemsettings1
    {
        public int NumberOfWeeks { get; set; }
        public int NumberOfDays { get; set; }
        public int WeekStart { get; set; }
        public int NumberOfSlots { get; set; }
        public bool MultiDesksAllowed { get; set; }
        public bool IncludeWeekEnds { get; set; }
        public int BookingPeriod { get; set; }
        public bool DeskBookingAllowed { get; set; }
        public bool DisableDeskDefaultLocation { get; set; }
        public int CanBookForOthersGlobal { get; set; }
    }

    public class Callresponse
    {
        public int ResponseCode { get; set; }
        public string ResponseMessage { get; set; }
    }

    public class Masterdata
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public object[] Locations { get; set; }
        public Locationsv2[] LocationsV2 { get; set; }
    }

    public class Locationsv2
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public string TimeZone { get; set; }
        public string LocationLatitude { get; set; }
        public string LocationLongitude { get; set; }
        public int TimeZoneOffsetInMins { get; set; }
        public bool EnableSelfCertification { get; set; }
        public string AssetURL { get; set; }
        public Wstype[] WSTypes { get; set; }
    }

    public class Wstype
    {
        public int WSTypeId { get; set; }
        public string WSTypeName { get; set; }
        public bool IsWorkplace { get; set; }
        public Group[] Groups { get; set; }
    }

    public class Group
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public Desksettings DeskSettings { get; set; }
        public Floor[] Floors { get; set; }
    }

    public class Desksettings
    {
        public bool BookMultipleDesk { get; set; }
        public bool CheckInRequired { get; set; }
        public bool AutoCheckIn { get; set; }
        public bool AutoBump { get; set; }
        public bool AllDay { get; set; }
        public int CheckInPeriodAM { get; set; }
        public int CheckInPeriodPM { get; set; }
        public int BumpTimeInMinutes { get; set; }
        public DateTime CheckInStartAM { get; set; }
        public DateTime CheckInStartPM { get; set; }
        public string CheckInStartTimeAM { get; set; }
        public string CheckInStartTimePM { get; set; }
        public bool CanBookForOthers { get; set; }
        public bool CanBookForExternalUser { get; set; }
        public bool PreventSpecificSpaceRequests { get; set; }
        public bool BookBestAvailable { get; set; }
        public bool HourlyGroup { get; set; }
        public string HourlyGroupBusinessStartTime { get; set; }
        public string HourlyGroupBusinessEndTime { get; set; }
        public bool SingleShiftBooking { get; set; }
        public Shift[] Shifts { get; set; }
    }

    public class Shift
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string StartTime { get; set; }
        public string EndTime { get; set; }
    }

    public class Floor
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public int FloorNum { get; set; }
    }

    public class Searchableattributesv2
    {
        public int ProfileItemID { get; set; }
        public string ItemLabel { get; set; }
        public int FieldType { get; set; }
        public bool IsUserDefaultAttribute { get; set; }
        public int ProfileSectionID { get; set; }
        public int WSTypeId { get; set; }
    }

    public class Profilesection
    {
        public int ProfileSectionID { get; set; }
        public string ProfileSectionName { get; set; }
    }

    public class Visitortype
    {
        public int PassTypeID { get; set; }
        public string PassTypeName { get; set; }
    }

}
