using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco
{
    public interface ICondeco
    {
        //server related
        public string BaseUrl { get; }

        //auth related
        public (bool Success, string ErrorMessage) LogIn(string username, string password);
        public (bool Success, string ErrorMessage) LogIn(string token);
        public void LogOut();

        //user related
        public string GetFullName();
        public bool CanBookForOthers(string locationName, string workspaceTypeName, string groupName);
        public bool CanBookForOthersExternal(string locationName, string workspaceTypeName, string groupName);

        //place related
        public List<Country> GetCountries();
        public List<Room> GetRooms(string countryName, string locationName, string groupName, string floorName, string workstationTypeName);

        //people related
        public List<Colleague> FindColleague(string searchTerm);

        //booking related
        public DateTime GetBookingWindowStartDate();
        public DateTime GetBookingWindowEndDate();
        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser);
        public List<UpcomingBooking> GetUpcomingBookings(DateOnly? fromDate = null, DateOnly? toDate = null);
        public (bool Success, string BookingStatusStr) CheckIn(UpcomingBooking bookingDetails);

    }
}
