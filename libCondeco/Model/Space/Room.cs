using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Space
{
    public class Room
    {
        public int Id;
        public string Name = "";
        public int WorkspaceTypeId;

        public int CountryId;
        public int LocationId;
        public int GroupId;
        public int FloorId;
    }
}
