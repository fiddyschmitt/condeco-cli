using libCondeco.Model.Space;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Web.Responses
{
    public class AppSettingResponse
    {
        public List<WorkspaceTypeDefinition> WorkspaceTypes = [];

        public static AppSettingResponse FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<AppSettingResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(AppSettingResponse)}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }

    public class WorkspaceTypeDefinition
    {
        public int Id;          //eg. 2 = Desk
        public string Name = "";
        public int ResourceId;  //eg. 128 = desk
    }
}
