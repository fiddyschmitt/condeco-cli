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
        public DateTime GetServerDateTimeUTC();

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

        //Declares the location/group pairs the caller intends to book, before the booking window is read.
        //The mobile API has no server-provided booking window, so it must compute one from a group's
        //advance-booking period - and those periods differ per group. Without this hint it has to guess
        //(it used the tenant's default group, whose period can be shorter than the booked group's, which
        //silently drops the newly-opened week). Ignored by implementations that read a real window.
        public void SetBookingScope(IEnumerable<(string LocationName, string GroupName)> scope);

        public DayOfWeek GetRolloverDay();
        public DateTime GetBookingWindowStartDate();
        public DateTime GetBookingWindowEndDate();
        public (DateTime StartDate, DateTime EndDate) GetBookingWindow();
        public Task<HttpResponseMessage> SendBookingRequest(Room room, DateOnly date, BookFor? bookForUser);

        public Task<HttpResponseMessage> SendBookingRequest(Room room, List<DateOnly> date, BookFor? bookForUser, string? tag = null, CancellationToken cancellationToken = default);
        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser);
        public bool BookingSuccessful(Room room, DateOnly bookedForDate, BookFor? bookingFor, CancellationToken cancellationToken = default);
        public List<UpcomingBooking> GetUpcomingBookings(DateOnly? fromDate = null, DateOnly? toDate = null);
        public (bool Success, string BookingStatusStr) CheckIn(UpcomingBooking bookingDetails);

    }
}
