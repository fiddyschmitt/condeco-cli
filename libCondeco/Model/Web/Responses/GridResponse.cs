using libCondeco.Model.Space;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class GridResponse
    {
        public List<Country> Countries = [];
        public Settings Settings = new();

        public static GridResponse FromServerResponse(string jsonStr)
        {
            if (string.IsNullOrEmpty(jsonStr)) return new();

            var result = JsonConvert.DeserializeObject<GridResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(Settings)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class Settings
    {
        public DeskSettings DeskSettings = new();
    }

    public class DeskSettings
    {
        //the window in which desks can be booked
        public DateTime StartDate;
        public DateTime EndDate;

        public bool IncludeWeekends;

        public string CheckInAMTime = "";
        public string CheckInPMTime = "";

        public int BusinessUnitManager;
    }
}
