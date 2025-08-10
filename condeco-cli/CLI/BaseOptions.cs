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
    }

    public enum EnumAPI
    {
        web,
        mobile
    }
}
