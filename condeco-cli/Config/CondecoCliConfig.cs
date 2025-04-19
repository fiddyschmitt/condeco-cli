using condeco_cli.Model;
using libCondeco.Extensions;
using libCondeco.Model.Space;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Config
{
    public class CondecoCliConfig
    {
        private readonly string configFilename;

        public CondecoCliConfig(string configFilename)
        {
            this.configFilename = configFilename;
            Reload();
        }

        public Account Account { get; private set; } = new()
        {
            BaseUrl = "",
            Username = "",
            Password = ""
        };

        public List<Booking> Bookings { get; private set; } = [];

        public void Save()
        {
            var ini = new Ini();

            ini["Account"]["BaseUrl"] = Account.BaseUrl;
            ini["Account"]["Username"] = Account.Username;
            ini["Account"]["Password"] = Account.Password;

            var bookingNumber = 1;
            Bookings
                .ForEach(booking =>
                {
                    var section = new Section()
                    {
                        SectionName = "Book"
                    };

                    ini.Sections.Add(section);

                    booking.AutogenName = $"Booking {bookingNumber++}";

                    section["Country"] = booking.Country;
                    section["Location"] = booking.Location;
                    section["Group"] = booking.Group;
                    section["Floor"] = booking.Floor;
                    section["WorkspaceType"] = booking.WorkspaceType;
                    section["Desk"] = booking.Desk;
                    section["Days"] = booking.Days.ToString(",");
                });

            var iniStr = ini.ToString();
            File.WriteAllText(configFilename, iniStr);
        }

        public void Reload()
        {
            var ini = new Ini();

            if (File.Exists(configFilename))
            {
                ini.Parse(File.ReadAllText(configFilename));
            }

            Account = new Account()
            {
                BaseUrl = ini["Account"]["BaseUrl"],
                Username = ini["Account"]["Username"],
                Password = ini["Account"]["Password"],
            };

            Bookings = ini
                        .Sections
                        .Where(section => section.SectionName.Equals("Book"))
                        .Select((section, index) => new Booking()
                        {
                            AutogenName = $"Booking {index + 1}",
                            Country = section["Country"],
                            Location = section["Location"],
                            Group = section["Group"],
                            Floor = section["Floor"],
                            WorkspaceType = section["WorkspaceType"],
                            Desk = section["Desk"],
                            Days = section["Days"].Split(",", StringSplitOptions.TrimEntries).ToList(),
                        })
                        .ToList();
        }
    }
}
