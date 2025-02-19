using libCondeco.Model.Space;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Queries
{
    public class RoomsResponse
    {
        public List<Room> Rooms;
        public static RoomsResponse FromServerResponse(int countryId, int locationId, int groupId, int floorId, string jsonStr)
        {
            var obj = JObject.Parse(jsonStr);

            var result = new RoomsResponse()
            {
                Rooms = obj["Rooms"]?
                                .Select(room => new Room()
                                {
                                    Id = room["RoomId"]?.Value<int>() ?? 0,
                                    Name = room["Name"]?.Value<string>() ?? "",
                                    WorkspaceTypeId = room["WSTypeId"]?.Value<int>() ?? 0,

                                    CountryId = countryId,
                                    LocationId = locationId,
                                    GroupId = groupId,
                                    FloorId = floorId,
                                })
                                .ToList() ?? []
            };

            return result;
        }
    }
}
