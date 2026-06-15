using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Web
{
    public interface IHttpClientFactory
    {
        //reLogin, when supplied, installs a ReAuthHandler at the top of the chain that re-authenticates
        //and retries on a 401. It returns the fresh bearer token (or null on failure).
        public HttpClient CreateClient(HttpMessageHandler handler, Func<string?>? reLogin = null);
    }
}
