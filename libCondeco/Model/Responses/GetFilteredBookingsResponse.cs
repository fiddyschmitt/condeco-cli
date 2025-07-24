using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class GetFilteredBookingsResponse
    {
        public Filter Filter { get; set; }
        public Meeting[] Meetings { get; set; }
        public int ResponseCode { get; set; }

        public static GetFilteredBookingsResponse FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<GetFilteredBookingsResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(GetFilteredBookingsResponse)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class Filter
    {
        public int CountryId { get; set; }
        public int LocationId { get; set; }
        public int GroupId { get; set; }
        public int FloorId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public string UserLongId { get; set; }
        public int UserId { get; set; }
        public int ViewType { get; set; }
        public int LanguageId { get; set; }
        public int ResourceType { get; set; }
        public int TimeFormat { get; set; }
        public bool ShowAMPM { get; set; }
        public object JobID { get; set; }
        public int WStypeId { get; set; }
    }

    public class Meeting
    {
        public int MeetingId { get; set; }
        public object Title { get; set; }
        public DateTime Start { get; set; }
        public DateTime End { get; set; }
        public Additionalinfo AdditionalInfo { get; set; }
        public int RoomId { get; set; }
        public int[] UserIds { get; set; }
        public bool CanViewBooking { get; set; }
        public bool CanEditBooking { get; set; }
        public bool CanDeleteBooking { get; set; }
        public bool IsPast { get; set; }
        public bool onGridPopup { get; set; }
        public bool onGridDisplay { get; set; }
        public object AdditionalInfoLimited { get; set; }
        public string EncryptedMeetingID { get; set; }
        public object EncryptedQuerstringReviewSummary { get; set; }
        public bool UserWorkFromHome { get; set; }
        public bool QRCodeEnabled { get; set; }
    }

    public class Additionalinfo
    {
        public string FullName { get; set; }
        public string Extension { get; set; }
        public int BookedBy { get; set; }
        public int DeskType { get; set; }
        public string BookingTypeColor { get; set; }
        public string Company { get; set; }
        public string EmailAddress { get; set; }
        public string Telephone { get; set; }
        public bool FlexiCheckInRequired { get; set; }
        public int BookingItemIDForCheckIn { get; set; }
        public bool CheckInAMRequired { get; set; }
        public bool CheckInPMRequired { get; set; }
        public int BookingOwnerUserID { get; set; }
        public int TeamDayId { get; set; }
        public int TeamDayOrganizerId { get; set; }
        public int BookingStatus { get; set; }
    }
}
