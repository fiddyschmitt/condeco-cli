using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Web
{
    public interface IHttpClientFactory
    {
        public HttpClient CreateClient(HttpMessageHandler handler);
    }
}
