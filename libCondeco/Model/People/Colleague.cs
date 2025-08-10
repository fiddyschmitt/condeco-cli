using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.People
{
    public class Colleague
    {
        public required string UserId { get; set; }
        public required string FullName { get; set; }
        public required string Email { get; set; }
    }
}
