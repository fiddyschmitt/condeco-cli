using libCondeco;
using libCondeco.Model.People;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Model
{
    public class Booking
    {
        public required string AutogenName;
        public required string Country;
        public required string Location;
        public required string Group;
        public required string Floor;
        public required string WorkspaceType;
        public required string Desk;
        public required List<string> Days = [];
        public required List<(DateOnly FromDate, DateOnly ToDate)> ExcludeDates = [];

        public BookFor? BookFor = null;

        public string GetBookingForFullName(ICondeco condeco)
        {
            if (BookFor == null || string.IsNullOrEmpty(BookFor.UserId))
                return condeco.GetFullName();

            return BookFor.FirstName + " " + BookFor.LastName;
        }
    }
}
