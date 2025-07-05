using libCondeco.Model.Space;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class FindAColleagueSearchResponse
    {
        public Colleague[] Colleagues = [];

        public static FindAColleagueSearchResponse FromServerResponse(string jsonStr)
        {
            var outerObj = JObject.Parse(jsonStr);
            var colleagueArrayJson = (outerObj["d"]?.ToString()) ?? throw new Exception($"Could not deserialize string to {nameof(FindAColleagueSearchResponse)}:{Environment.NewLine}{jsonStr}");

            var result = new FindAColleagueSearchResponse()
            {
                Colleagues = JsonConvert.DeserializeObject<Colleague[]>(colleagueArrayJson)
                            ?? throw new Exception($"Could not deserialize string to {nameof(Colleague)}:{Environment.NewLine}{colleagueArrayJson}")
            };

            return result;
        }

        public class Colleague
        {
            public required string Department { get; set; }
            public required string Email { get; set; }
            public required string FullName { get; set; }
            public required bool IsHideDeskAndWorkStatus { get; set; }
            public required bool IsRoleAssigned { get; set; }
            public required bool IsTeamMember { get; set; }
            public required object RoleList { get; set; }
            public required string Telephone { get; set; }
            public required string TelephoneExt { get; set; }
            public required int UserID { get; set; }
        }
    }
}

