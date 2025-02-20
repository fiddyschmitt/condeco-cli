using libCondeco.Model.Space;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Responses
{
    public class GetAppSettingResponse
    {
        public List<WorkspaceTypeDefinition> WorkspaceTypes = [];

        public static GetAppSettingResponse FromServerResponse(string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<GetAppSettingResponse>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {nameof(GetAppSettingResponse)}:{Environment.NewLine}{jsonStr}");

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
