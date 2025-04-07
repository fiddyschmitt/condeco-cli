using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    [Verb("--dump", HelpText = "Dumps condeco metadata to the outputs folder.")]
    class DumpOptions
    {
        [Option("config", Required = false, HelpText = "Path to the config file.")]
        public string Config { get; set; } = "";
    }
}
