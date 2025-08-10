using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Common
{
    public class Callresponse
    {
        public int ResponseCode { get; set; }
        public required string ResponseMessage { get; set; }
    }
}
