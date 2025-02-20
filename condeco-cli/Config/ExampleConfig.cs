using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Config
{
    public static class ExampleConfig
    {
        public static string ExampleString = """
[Account]
BaseUrl=https://acme.condecosoftware.com
Username=
Password=

[Book]
Country=
Location=
Group=
Floor=
WorkspaceType=
Desk=
Days=Monday,Tuesday,Wednesday

[Book]
Country=
Location=
Group=
Floor=
WorkspaceType=
Desk=
Days=Thursday,Friday
""";
    }
}
