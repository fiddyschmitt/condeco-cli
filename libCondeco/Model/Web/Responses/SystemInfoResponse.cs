using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class SystemInfoResponse
    {
        public required string apiVersion { get; set; }
        public required string appVersion { get; set; }
        public bool isAzure { get; set; }
        public required Authinfo authInfo { get; set; }
        public string? roomFinderMode { get; set; }
        public bool passwordlessAuthEnabled { get; set; }
        public bool enableSCIntellegentAttendee { get; set; }

        //Eptura One ("platform") tenants only. Classic Condeco tenants omit these, so they are nullable.
        public bool isPlatform { get; set; }
        public string? platformPingURL { get; set; }
        public string? platformAuthURL { get; set; }
        public string? platformRef { get; set; }
        public string? platformId { get; set; }
        public string? platformBaseURL { get; set; }


        public class Authinfo
        {
            public required string type { get; set; }
        }

    }
}
