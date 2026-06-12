namespace libCondeco.EpturaOne
{
    //One entry per tenant returned by GET {pingHost}/api/v1/auth/app?emailId=<email>
    public class PlatformPingResponse
    {
        public string TenantId { get; set; } = "";
        public string TenantName { get; set; } = "";
        public string TenantCode { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string AppConfigId { get; set; } = "";
        public string ApplicationId { get; set; } = "";
        public bool IsFormUser { get; set; }
        public bool HasActiveLicense { get; set; }
        public bool HasActiveAppAccess { get; set; }
        public bool IsDisabled { get; set; }
    }
}
