using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.CLI
{
    [Verb("--autobook", HelpText = "Automatically book the rooms specified in config.ini")]
    public class AutoBookOptions : BaseOptions
    {
        [Option("wait-for-rollover", Required = false, HelpText = "Wait for the new booking window to be available. Specify the number of minutes to wait for. Max 30 minutes.")]
        public int? WaitForRolloverMinutes { get; set; } = null;

        public const int MAX_WAIT_DURATION_MINUTES = 30;
    }
}
