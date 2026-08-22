using libCondeco.Model.Bookings;
using libCondeco.Model.Common;
using libCondeco.Model.People;
using libCondeco.Model.Space;

namespace libCondeco
{
    //Books through the mobile API while reading the booking window through a web session.
    //
    //The mobile API exposes no authoritative booking window - it can only be inferred from the
    //per-group "advance period" settings, which proved unreliable twice (10-Aug-2026: the group's
    //advance period said 2 weeks while the server happily accepted bookings 3 weeks out; the field is
    //decorative config, not enforcement). The web API reports the real window via the grid's desk
    //settings. This class pairs the two: everything booking-related goes to the mobile API (the path
    //being exercised), while the window - and the rollover-expansion detection built on it - comes
    //from web-server truth.
    //
    //If the web login fails, the run degrades to the mobile-inferred window rather than aborting: a
    //wrong-but-plausible window is better than no booking run at all, and the failure is logged.
    public class CondecoHybrid : ICondeco
    {
        private readonly CondecoMobile mobile;
        private readonly CondecoWeb web;

        private bool webAvailable = false;

        public CondecoHybrid(CondecoMobile mobile, CondecoWeb web)
        {
            this.mobile = mobile;
            this.web = web;
        }

        public string BaseUrl => mobile.BaseUrl;

        public (bool Success, string ErrorMessage) LogIn(string username, string password)
        {
            var mobileResult = mobile.LogIn(username, password);
            if (!mobileResult.Success)
            {
                return mobileResult;
            }

            var (webSuccess, webError) = web.LogIn(username, password);
            webAvailable = webSuccess;

            if (!webSuccess)
            {
                Console.WriteLine($"{DateTime.Now}  Web session for booking-window detection could not be established ({webError}). Falling back to the mobile-inferred window, which may be inaccurate.");
            }

            return mobileResult;
        }

        public (bool Success, string ErrorMessage) LogIn(string token)
        {
            //Token login is mobile-only; without a web session the window falls back to inference.
            webAvailable = false;
            return mobile.LogIn(token);
        }

        public void LogOut()
        {
            mobile.LogOut();

            if (webAvailable)
            {
                web.LogOut();
            }
        }

        //Window truth comes from the web grid's desk settings whenever the web session is up.
        public DayOfWeek GetRolloverDay() => webAvailable ? web.GetRolloverDay() : mobile.GetRolloverDay();
        public DateTime GetBookingWindowStartDate() => webAvailable ? web.GetBookingWindowStartDate() : mobile.GetBookingWindowStartDate();
        public DateTime GetBookingWindowEndDate() => webAvailable ? web.GetBookingWindowEndDate() : mobile.GetBookingWindowEndDate();
        public (DateTime StartDate, DateTime EndDate) GetBookingWindow() => webAvailable ? web.GetBookingWindow() : mobile.GetBookingWindow();

        //Both sides get the scope: the web implementation is a no-op, and the mobile one only matters
        //when the window has fallen back to inference.
        public void SetBookingScope(IEnumerable<(string LocationName, string GroupName)> scope)
        {
            var scopeList = scope.ToList();
            mobile.SetBookingScope(scopeList);
            web.SetBookingScope(scopeList);
        }

        //Everything else - bookings, confirmations, rooms, people, check-in - is the mobile API.
        public DateTime GetServerDateTimeUTC() => mobile.GetServerDateTimeUTC();
        public string GetFullName() => mobile.GetFullName();
        public bool CanBookForOthers(string locationName, string workspaceTypeName, string groupName) => mobile.CanBookForOthers(locationName, workspaceTypeName, groupName);
        public bool CanBookForOthersExternal(string locationName, string workspaceTypeName, string groupName) => mobile.CanBookForOthersExternal(locationName, workspaceTypeName, groupName);
        public List<Country> GetCountries() => mobile.GetCountries();
        public List<Room> GetRooms(string countryName, string locationName, string groupName, string floorName, string workstationTypeName) => mobile.GetRooms(countryName, locationName, groupName, floorName, workstationTypeName);
        public List<Colleague> FindColleague(string searchTerm) => mobile.FindColleague(searchTerm);
        public Task<HttpResponseMessage> SendBookingRequest(Room room, DateOnly date, BookFor? bookForUser) => mobile.SendBookingRequest(room, date, bookForUser);
        public Task<HttpResponseMessage> SendBookingRequest(Room room, List<DateOnly> dates, BookFor? bookForUser, string? tag = null, CancellationToken cancellationToken = default) => mobile.SendBookingRequest(room, dates, bookForUser, tag, cancellationToken);
        public (bool Success, BookingResponse BookingResponse) BookRoom(Room room, DateOnly date, BookFor? bookForUser) => mobile.BookRoom(room, date, bookForUser);
        public bool BookingSuccessful(Room room, DateOnly bookedForDate, BookFor? bookingFor, CancellationToken cancellationToken = default) => mobile.BookingSuccessful(room, bookedForDate, bookingFor, cancellationToken);
        public List<UpcomingBooking> GetUpcomingBookings(DateOnly? fromDate = null, DateOnly? toDate = null) => mobile.GetUpcomingBookings(fromDate, toDate);
        public (bool Success, string BookingStatusStr) CheckIn(UpcomingBooking bookingDetails) => mobile.CheckIn(bookingDetails);
    }
}
