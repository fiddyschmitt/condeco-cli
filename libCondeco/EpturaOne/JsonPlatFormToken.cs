namespace libCondeco.EpturaOne
{
    //Response of GET {tenant}/MobileAPI/mobileservice.svc/login/ValidatePlatformToken
    //(the Eptura One platform-token exchange). JSON keys: Success, token, sessionToken.
    public class JsonPlatFormToken
    {
        public bool Success { get; set; }

        //The platform access token — used as the "Bearer" Authorization on subsequent calls.
        public string Token { get; set; } = "";

        //The platform session token — used as the sessionGuid (the session credential).
        public string SessionToken { get; set; } = "";
    }
}
