using libCondeco.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Mobile.Responses
{
    public class FloorPlanResponse
    {
        public required Callresponse CallResponse { get; set; }
        public required Floorplan FloorPlan { get; set; }
    }

    public class Floorplan
    {
        public int FloorNumber { get; set; }
        public required string Name { get; set; }
        public required string NameBlobUrl { get; set; }
        public required Resourcecoordinate[] ResourceCoordinates { get; set; }
    }

    public class Resourcecoordinate
    {
        public required string ClosedFrom { get; set; }
        public required string ClosedUntil { get; set; }
        public required Deskattribute[] DeskAttributes { get; set; }
        public bool IsWorkplace { get; set; }
        public int ResourceItemId { get; set; }
        public required string ResourceItemName { get; set; }
        public int SanitizationStatus { get; set; }
        public int WSTypeID { get; set; }
        public required string WSTypeName { get; set; }
        public int XPos { get; set; }
        public int YPos { get; set; }
    }

    public class Deskattribute
    {
        public int AttributeID { get; set; }
        public required string AttributeName { get; set; }
    }

}
