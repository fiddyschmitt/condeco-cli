using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Mobile.Responses
{
    public class ListV2Response
    {
        public bool Success { get; set; }
        public int RoomRecordsCount { get; set; }
        public required object[] RoomBookings { get; set; }
        public int DeskRecordsCount { get; set; }
        public required Deskbooking[] DeskBookings { get; set; }
        public bool EditMiniSyncBooking { get; set; }
        public required string UName { get; set; }
        public required Error Error { get; set; }
    }

    public class Deskbooking
    {
        public int BookingID { get; set; }
        public int BookingType { get; set; }
        public required string BookingStart { get; set; }
        public required string BookingEnd { get; set; }
        public int TimeZoneOffSetInMins { get; set; }
        public required string TimeZone { get; set; }
        public int Status { get; set; }
        public required AdditionalInformation AdditionalInformation { get; set; }
        public int FloorID { get; set; }
        public required string FloorName { get; set; }
        public int CountryID { get; set; }
        public int LocationID { get; set; }
        public required string LocationName { get; set; }
        public int GroupID { get; set; }
        public required string GroupName { get; set; }
        public int DeskID { get; set; }
        public required string DeskName { get; set; }
        public bool CanBeBooked { get; set; }
        public required object DeskAttributes { get; set; }
        public int WSTypeId { get; set; }
        public required string WsTypeName { get; set; }
        public bool QRCodeEnabled { get; set; }
        public bool bookMultipleDesk { get; set; }
        public int deskType { get; set; }
        public int ShiftId { get; set; }
    }

    public class AdditionalInformation
    {
        public int UserID;
        public required string FullName;
    }

}
