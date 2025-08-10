using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using libCondeco.Model.Common;

namespace libCondeco.Model.Mobile.Responses
{

    public class LoginInformationsV2Response
    {
        public required Roomresults RoomResults { get; set; }
        public required Deskresults DeskResults { get; set; }
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
        public required string UserPin { get; set; }
        public required string UserEmail { get; set; }
        public int AIBookingEnabledForUser { get; set; }
        public bool QRCodeEnabled { get; set; }
        public bool AddToOutlookVisible { get; set; }
        public bool AddToOutlookChecked { get; set; }
        public bool SCSubscriptionStatus { get; set; }
        public bool EnableVisitor { get; set; }
        public required Visitortype[] VisitorType { get; set; }
        public bool EnableConcierge { get; set; }
        public required object[] ConciergeLocationList { get; set; }
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
        public required string SessionGUID { get; set; }
        public required Systemsettings SystemSettings { get; set; }
        public required Error Error { get; set; }
    }

    public class Systemsettings
    {
        public bool RoomBookingAllowed { get; set; }
    }

    public class Error
    {
        public int ErrorCode { get; set; }
        public required string ErrorDescription { get; set; }
    }

    public class Deskresults
    {
        public required string AccessToken { get; set; }
        public required Masterdata[] MasterData { get; set; }
        public required Defaultsettings DefaultSettings { get; set; }
        public required Systemsettings1 SystemSettings { get; set; }
        public required object[] Bookings { get; set; }
        public required string UserFirstName { get; set; }
        public required string UserLastName { get; set; }
        public required string UName { get; set; }
        public required object[] SearchableAttributes { get; set; }
        public required Searchableattributesv2[] SearchableAttributesV2 { get; set; }
        public required Profilesection[] ProfileSections { get; set; }
        public required object[] Roles { get; set; }
        public required object[] SearchableAttributesHPS { get; set; }
        public required Callresponse CallResponse { get; set; }
    }

    public class Defaultsettings
    {
        public int DefaultCountry { get; set; }
        public required string DefaultFloor { get; set; }
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
    public class Masterdata
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public required object[] Locations { get; set; }
        public required Locationsv2[] LocationsV2 { get; set; }
    }

    public class Locationsv2
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public required string TimeZone { get; set; }
        public required string LocationLatitude { get; set; }
        public required string LocationLongitude { get; set; }
        public int TimeZoneOffsetInMins { get; set; }
        public bool EnableSelfCertification { get; set; }
        public required string AssetURL { get; set; }
        public required Wstype[] WSTypes { get; set; }
    }

    public class Wstype
    {
        public int WSTypeId { get; set; }
        public required string WSTypeName { get; set; }
        public bool IsWorkplace { get; set; }
        public required Group[] Groups { get; set; }
    }

    public class Group
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public required Desksettings DeskSettings { get; set; }
        public required Floor[] Floors { get; set; }
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
        public required string CheckInStartTimeAM { get; set; }
        public required string CheckInStartTimePM { get; set; }
        public bool CanBookForOthers { get; set; }
        public bool CanBookForExternalUser { get; set; }
        public bool PreventSpecificSpaceRequests { get; set; }
        public bool BookBestAvailable { get; set; }
        public bool HourlyGroup { get; set; }
        public required string HourlyGroupBusinessStartTime { get; set; }
        public required string HourlyGroupBusinessEndTime { get; set; }
        public bool SingleShiftBooking { get; set; }
        public required Shift[] Shifts { get; set; }
    }

    public class Shift
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public required string StartTime { get; set; }
        public required string EndTime { get; set; }
    }

    public class Floor
    {
        public int ID { get; set; }
        public required string Name { get; set; }
        public int FloorNum { get; set; }
    }

    public class Searchableattributesv2
    {
        public int ProfileItemID { get; set; }
        public required string ItemLabel { get; set; }
        public int FieldType { get; set; }
        public bool IsUserDefaultAttribute { get; set; }
        public int ProfileSectionID { get; set; }
        public int WSTypeId { get; set; }
    }

    public class Profilesection
    {
        public int ProfileSectionID { get; set; }
        public required string ProfileSectionName { get; set; }
    }

    public class Visitortype
    {
        public int PassTypeID { get; set; }
        public required string PassTypeName { get; set; }
    }

}
