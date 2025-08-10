using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.People
{
    public class BookFor
    {
        public required string UserId { get; set; }
        public required string FirstName { get; set; }
        public required string LastName { get; set; }
        public string Company { get; set; } = "";
        public required string EmailAddress { get; set; }
        public required string IsExternal { get; set; }

        public string ToGeneralFormString()
        {
            var result = $"fkUserID~{UserId}¬firstName~{FirstName}¬lastName~{LastName}¬company~{Company}¬emailAddress~{EmailAddress}¬telephone~¬isExternal~{IsExternal}¬notifyByPhone~0¬notifyByEmail~0¬notifyBySMS~¬";
            return result;
        }

        public static BookFor CurrentUser()
        {
            var result = new BookFor()
            {
                UserId = "",
                FirstName = "",
                LastName = "",
                EmailAddress = "",
                IsExternal = "0"
            };

            return result;
        }
    }
}