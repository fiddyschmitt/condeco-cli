using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Mobile.Responses
{
    public class AuthenticateUserSecureResponse
    {
        public int ResponseCode { get; set; }
        public required object ResponseData { get; set; }
        public required Result Result { get; set; }
    }

    public class Result
    {
        public int LoginResult { get; set; }
        public required string MemorableWord { get; set; }
        public required string Token { get; set; }
        public required object UserInformation { get; set; }
    }
}
