using libCondeco.Model.Responses;
using libCondeco.Model.Space;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Queries
{
    public class GridResponse
    {
        public List<Country> Countries = [];
        public Settings Settings = new();

        public static GridResponse FromServerResponse(string jsonStr)
        {
            var obj = JObject.Parse(jsonStr);

            var result = new GridResponse()
            {
                //DeskSettings = new DeskSettings()
                //{
                //    StartDate = obj["DeskSettings"]["StartDate"]?.Value<DateTime>() ?? DateTime.Today,
                //    EndDate = obj["DeskSettings"]["EndDate"]?.Value<DateTime>() ?? DateTime.Today.AddDays(21)
                //},

                Settings = JsonConvert.DeserializeObject<Settings>(obj["Settings"].ToString()),

                Countries = obj["Countries"]?
                                .Select(country => new Country()
                                {
                                    Id = country["Id"]?.Value<int>() ?? 0,
                                    Name = country["Name"]?.Value<string>() ?? "",

                                    Locations = country["Locations"]?
                                                    .Select(location => new Location()
                                                    {
                                                        Id = location["Id"]?.Value<int>() ?? 0,
                                                        Name = location["Name"]?.Value<string>() ?? "",

                                                        Groups = location["Groups"]?
                                                                    .Select(group => new Group()
                                                                    {
                                                                        Id = group["Id"]?.Value<int>() ?? 0,
                                                                        Name = group["Name"]?.Value<string>() ?? "",

                                                                        Floors = group["Floors"]?
                                                                                    .Select(floor => new Floor()
                                                                                    {
                                                                                        Id = floor["Id"]?.Value<int>() ?? 0,
                                                                                        Name = floor["Name"]?.Value<string>() ?? "",

                                                                                        WorkspaceTypes = floor["WorkspaceTypes"]?
                                                                                                    .Select(workspaceType => new WorkspaceType()
                                                                                                    {
                                                                                                        Id = workspaceType["Id"]?.Value<int>() ?? 0,
                                                                                                        Name = workspaceType["Name"]?.Value<string>() ?? "",
                                                                                                    })
                                                                                                    ?.ToList() ?? []
                                                                                    })
                                                                                    ?.ToList() ?? []


                                                                    })
                                                                    ?.ToList() ?? []
                                                    })
                                                    ?.ToList() ?? []
                                })
                                ?.ToList() ?? []
            };

            return result;
        }
    }

    public class Settings
    {
        public DeskSettings DeskSettings = new();
    }

    public class DeskSettings
    {
        public DateTime StartDate;
        public DateTime EndDate;
    }
}
