using libCondeco.Model.Space;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class RoomsResponse
    {
        public List<Room> Rooms = [];
        public static RoomsResponse FromServerResponse(int countryId, int locationId, int groupId, int floorId, string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<RoomsResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(RoomsResponse)}:{Environment.NewLine}{jsonStr}");

            //populate the extra metadata
            foreach (var room in result.Rooms)
            {
                room.CountryId = countryId;
                room.LocationId = locationId;
                room.GroupId = groupId;
                room.FloorId = floorId;
            }

            return result;
        }
    }
}
