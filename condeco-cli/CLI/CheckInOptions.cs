using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    [Verb("--checkin", HelpText = "Check in to the booked desks")]
    class CheckInOptions
    {
        [Option("config", Required = false, HelpText = "Path to the config file.")]
        public string Config { get; set; } = "";
    }
}
