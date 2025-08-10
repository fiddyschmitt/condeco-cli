using libCondeco.Model.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Model.Mobile.Responses
{
    public class GroupSettingsWithRestrictions
    {
        public required Callresponse CallResponse { get; set; }
        public int advancePeriodUnit { get; set; }
        public int advancePeriodValue { get; set; }
        public bool bookMultipleDesk { get; set; }
        public int hpsDefaultPeriod { get; set; }
        public bool includeWeekend { get; set; }
        public bool isRollOverWeekStarted { get; set; }
        public int maximumSelectableWeeks { get; set; }
        public int restrictionType { get; set; }
        public int slotsPerMonth { get; set; }
        public int slotsPerWeek { get; set; }
    }
}
