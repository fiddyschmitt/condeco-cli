using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    [Verb("--autocheckin", HelpText = "Automatically check in to today's bookings.")]
    class AutoCheckinOptions
    {
        [Option("config", Required = false, HelpText = "Path to the config file.")]
        public string Config { get; set; } = "";
    }
}
