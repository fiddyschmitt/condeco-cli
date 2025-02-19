using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    public static class ExampleConfig
    {
        public static string ExampleString = """
[Account]
BaseUrl=https://acme.condeco.com
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
