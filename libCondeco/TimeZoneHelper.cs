using TimeZoneConverter;

namespace libCondeco
{
    public static class TimeZoneHelper
    {
        //The Condeco mobile/room APIs want an IANA timezone id (e.g. "Australia/Sydney"). On Windows,
        //TimeZoneInfo.Local.Id is a WINDOWS id ("E. Australia Standard Time") that must be converted; on
        //Linux/macOS it is ALREADY an IANA id, and feeding that to WindowsToIana throws
        //InvalidTimeZoneException. That silently broke every mobile confirmation on Linux, and crashed
        //--checkin --api mobile / --dump outright (24-Aug-2026). Convert only when the id really is a
        //Windows id; otherwise it is already IANA and is used as-is.
        public static string LocalIana()
        {
            var localId = TimeZoneInfo.Local.Id;
            return TZConvert.TryWindowsToIana(localId, out var iana) ? iana : localId;
        }
    }
}
