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
        public required string roomFinderMode { get; set; }
        public bool passwordlessAuthEnabled { get; set; }
        public bool enableSCIntellegentAttendee { get; set; }
        public bool isPlatform { get; set; }
        public required string platformPingURL { get; set; }
        public required string platformAuthURL { get; set; }
        public required string platformRef { get; set; }
        public required string platformId { get; set; }


        public class Authinfo
        {
            public required string type { get; set; }
        }

    }
}
