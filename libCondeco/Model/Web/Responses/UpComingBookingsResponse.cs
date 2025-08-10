using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class UpComingBookingsResponse
    {
        public List<UpComingBooking> UpComingBookings = [];

        public static UpComingBookingsResponse FromServerResponse(string jsonStr)
        {
            var upcomingBookingsArray = JsonConvert.DeserializeObject<JArray>(jsonStr,
                                            new JsonSerializerSettings
                                            {
                                                DateParseHandling = DateParseHandling.None
                                            });

            var result = new UpComingBookingsResponse()
            {
                UpComingBookings = upcomingBookingsArray?
                                    .Children<JObject>()
                                    .Select(bookingObj =>
                                    {
                                        var bookingJSON = bookingObj.ToString();
                                        var booking = JsonConvert.DeserializeObject<UpComingBooking>(bookingJSON)
                                                        ?? throw new Exception($"Could not deserialize string to {nameof(UpComingBooking)}:{Environment.NewLine}{bookingJSON}");

                                        return booking;
                                    })
                                    .ToList() ?? throw new Exception($"Could not deserialize string to {nameof(UpComingBookingsResponse)}:{Environment.NewLine}{jsonStr}")
            };

            return result;
        }
    }

    public class UpComingBooking
    {
        public int bookingType { get; set; }
        public required string bookingTitle { get; set; }
        public required string bookedResourceName { get; set; }
        public int bookedResourceItemId { get; set; }
        public required string floor { get; set; }
        public required string bookedLocation { get; set; }
        public int bookingId { get; set; }
        public int bookingItemId { get; set; }
        public required Bookedby bookedBy { get; set; }
        public required object requestedBy { get; set; }
        public required Bookedfor bookedFor { get; set; }
        public DateTime startDateTime { get; set; }
        public DateTime endDateTime { get; set; }
        public DateTime startDateTimeUtc { get; set; }
        public DateTime endDateTimeUtc { get; set; }
        public int bookingStatus { get; set; }
        public int businessUnitId { get; set; }
        public int locationId { get; set; }
        public required string locationTimeZone { get; set; }
        public required object parentBookingId { get; set; }
        public required object vcId { get; set; }
        public bool approved { get; set; }
        public bool pendingOnGrid { get; set; }
        public bool waitList { get; set; }
        public bool blindManaged { get; set; }
        public bool managedForAdmin { get; set; }
        public int resourceStatus { get; set; }
        public required object encryptedQuerstringReviewSummary { get; set; }
        public required object amReleased { get; set; }
        public required object pmReleased { get; set; }
        public required object fdCheckedIn { get; set; }
        public required object fdReleased { get; set; }
        public required Bookingmetadata bookingMetadata { get; set; }
        public required object minutesToExtend { get; set; }
        public required object updateDateTime { get; set; }
        public required object updateDateTimeUtc { get; set; }
        public int bookingSource { get; set; }
        public int enterpriseBookingSource { get; set; }
        public bool individuallyEdited { get; set; }
        public required string timeZoneId { get; set; }
        public int changedByUserId { get; set; }
        public bool enableSelfCertificationOnLocation { get; set; }
        public required string selfCertificationContent { get; set; }
        public required string selfCertificationDisagreeContent { get; set; }
        public int enableDeskCheckinLocationTimeZone { get; set; }
        public int wsTypeId { get; set; }
        public bool qrCodeEnabled { get; set; }
        public bool disableWebCheckIn { get; set; }
        public required object assetURL { get; set; }
        public required string floorName { get; set; }
        public required object isWorkplace { get; set; }
        public int recurrenceID { get; set; }
        public int languageId { get; set; }
        public bool floorPlanOnMap { get; set; }
        public bool preventSpecificSpaceRequests { get; set; }
        public bool enableMeetingSpacesFloorPlan { get; set; }
    }

    public class Bookedby
    {
        public int userId { get; set; }
        public required string name { get; set; }
        public required string requestorEmail { get; set; }
    }

    public class Bookedfor
    {
        public int userId { get; set; }
        public required string name { get; set; }
        public required object requestorEmail { get; set; }
    }

    public class Bookingmetadata
    {
        public required object attendees { get; set; }
        public required Rules rules { get; set; }
        public bool isCurrentUserMeetingAdmin { get; set; }
    }

    public class Rules
    {
        public int businessUnitId { get; set; }
        public int bookingTimeFormat { get; set; }
        public bool hotDeskMultipleDesks { get; set; }
        public int hdAmPlusMinutes { get; set; }
        public int hdPmPlusMinutes { get; set; }
        public bool hdCheckInRequired { get; set; }
        public bool hdAllDayCheckin { get; set; }
        public bool hdSameDayAutoCheckin { get; set; }
        public bool hdAutoBump { get; set; }
        public int hdCheckInAmStartHour { get; set; }
        public int hdCheckInAmStartMinutes { get; set; }
        public int hdCheckInPmStartHour { get; set; }
        public int hdCheckInPmStartMinutes { get; set; }
        public DateTime hdCheckInAmStartUtc { get; set; }
        public DateTime hdCheckInAmEndUtc { get; set; }
        public DateTime hdCheckInPmStartUtc { get; set; }
        public DateTime hdCheckInPmEndUtc { get; set; }
        public int businessHourStartTimeHour { get; set; }
        public int businessHourStartTimeMinutes { get; set; }
        public DateTime businessHourStartTimeUtc { get; set; }
        public int businessHourEndTimeHour { get; set; }
        public int businessHourEndTimeMinutes { get; set; }
        public DateTime businessHourEndTimeUtc { get; set; }
        public bool autoStartMeeting { get; set; }
        public int screenProfileID { get; set; }
        public bool autoCloseBooking { get; set; }
        public int deleteBookingAfterMinutes { get; set; }
        public bool roomNoShow { get; set; }
        public int autoShrink { get; set; }
        public int autoShrinkSlots { get; set; }
        public int roomProgressInMinutes { get; set; }
        public int defaultSlotTime { get; set; }
        public int cancelBeforeDays { get; set; }
        public required object cancelBeforeTime { get; set; }
        public bool hideRoomNameForUser { get; set; }
        public int managedBookingNotificationInMinutes { get; set; }
        public int groupMeetingProgression { get; set; }
        public required object hpsCheckInbeforeTimePeriod { get; set; }
        public required object hpsCheckInbeforeTimeUnit { get; set; }
        public required object hpsBumpTimePeriod { get; set; }
        public required object hpsBumpTimeUnit { get; set; }
    }

}
