using libCondeco.Model.Web.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Space
{
    public class Country
    {
        public int Id;
        public string Name = "";

        public List<Location> Locations = [];
    }
}
