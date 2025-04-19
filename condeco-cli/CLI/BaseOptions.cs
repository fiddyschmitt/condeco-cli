using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    public class BaseOptions
    {
        [Option("config", Required = false, HelpText = "Path to the config file.")]
        public string Config { get; set; } = "";
    }
}
