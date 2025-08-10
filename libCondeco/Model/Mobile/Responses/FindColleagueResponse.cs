using libCondeco.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Mobile.Responses
{
    public class FindColleagueResponse
    {
        public required Callresponse CallResponse { get; set; }
        public required Userdetail[] userDetails { get; set; }

        public class Userdetail
        {
            public required string Department { get; set; }
            public required string Email { get; set; }
            public required string FullName { get; set; }
            public bool IsHideDeskAndWorkStatus { get; set; }
            public bool IsRoleAssigned { get; set; }
            public bool IsTeamMember { get; set; }
            public required string[] RoleList { get; set; }
            public required string Telephone { get; set; }
            public required string TelephoneExt { get; set; }
            public int UserID { get; set; }
        }

    }
}
