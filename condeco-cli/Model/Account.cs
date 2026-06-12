using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Model
{
    public class Account
    {
        public required string BaseUrl;

        public string Username = "";
        public string Password = "";

        public string RefreshToken = "";

        //SSO/Eptura One accounts are identified purely by the presence of a refresh token (the only
        //durable credential they have); username/password accounts never have one.
        public bool IsSsoAccount => !string.IsNullOrEmpty(RefreshToken);

        //Whether usable credentials are configured (username/password, or an SSO account).
        public bool IsConfigured => !string.IsNullOrEmpty(Username) || IsSsoAccount;
    }
}
