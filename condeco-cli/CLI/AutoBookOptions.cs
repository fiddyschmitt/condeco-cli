﻿using CommandLine;
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
        [Option("wait-for-rollover", Required = false, HelpText = "Continously query the server until the new booking window becomes available. Specify the number of minutes to poll for. Max 5 minutes.")]
        public int? WaitForRolloverMinutes { get; set; } = null;
    }
}
