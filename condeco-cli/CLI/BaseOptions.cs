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

        [Option("api", Required = false, HelpText = "The API to use. Choose either 'web' or 'mobile'. Default is web.")]
        public EnumAPI API { get; set; } = EnumAPI.web;

        [Option("autobook", Required = false, HelpText = "Automatically book the rooms specified in config.")]
        public bool AutoBook { get; set; }

        [Option("checkin", Required = false, HelpText = "Check in to the booked desks.")]
        public bool CheckIn { get; set; }

        [Option("dump", Required = false, HelpText = "Dump condeco metadata to the outputs folder.")]
        public bool Dump { get; set; }

        [Option("wait-for-rollover", Required = false, HelpText = "(--autobook only) Wait for the new booking window to be available. Specify the number of minutes to wait for. Max 30 minutes.")]
        public int? WaitForRolloverMinutes { get; set; } = null;

        [Option("verbose", Required = false, HelpText = "Log HTTP requests and responses to console.")]
        public bool Verbose { get; set; }

        public const int MAX_WAIT_DURATION_MINUTES = 30;
    }

    public enum EnumAPI
    {
        web,
        mobile
    }
}
