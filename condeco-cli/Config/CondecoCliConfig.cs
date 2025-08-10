using condeco_cli.Model;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.People;
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
            BaseUrl = ""
        };

        public List<Booking> Bookings { get; private set; } = [];

        public void Save()
        {
            var ini = new Ini();

            ini["Account"]["BaseUrl"] = Account.BaseUrl;

            if (!string.IsNullOrEmpty(Account.Username))
            {
                ini["Account"]["Username"] = Account.Username;
                ini["Account"]["Password"] = Account.Password;
            }
            else if (!string.IsNullOrEmpty(Account.Token))
            {
                {
                    ini["Account"]["Token"] = Account.Token;
                }
            }

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

                    if (booking.BookFor != null && !string.IsNullOrEmpty(booking.BookFor.UserId))
                    {
                        section["BookFor_UserID"] = booking.BookFor.UserId;
                        section["BookFor_FirstName"] = booking.BookFor.FirstName;
                        section["BookFor_LastName"] = booking.BookFor.LastName;
                        section["BookFor_Company"] = booking.BookFor.Company;
                        section["BookFor_Email"] = booking.BookFor.EmailAddress;
                        section["BookFor_IsExternal"] = booking.BookFor.IsExternal;
                    }
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

            };

            if (!string.IsNullOrEmpty(ini["Account"]["Username"]))
            {
                Account.Username = ini["Account"]["Username"];
                Account.Password = ini["Account"]["Password"];
            }
            else if (!string.IsNullOrEmpty(ini["Account"]["Token"]))
            {
                Account.Token = ini["Account"]["Token"];
            }

            Bookings = ini
                        .Sections
                        .Where(section => section.SectionName.Equals("Book"))
                        .Select((section, index) =>
                        {
                            BookFor? bookFor = null;
                            if (!string.IsNullOrEmpty(section["BookFor_UserID"]))
                            {
                                bookFor = new BookFor()
                                {
                                    UserId = section["BookFor_UserID"],
                                    FirstName = section["BookFor_FirstName"],
                                    LastName = section["BookFor_LastName"],
                                    Company = section["BookFor_Company"],
                                    EmailAddress = section["BookFor_Email"],
                                    IsExternal = section["BookFor_IsExternal"]
                                };
                            }

                            return new Booking()
                            {
                                AutogenName = $"Booking {index + 1}",
                                Country = section["Country"],
                                Location = section["Location"],
                                Group = section["Group"],
                                Floor = section["Floor"],
                                WorkspaceType = section["WorkspaceType"],
                                Desk = section["Desk"],
                                Days = section["Days"].Split(",", StringSplitOptions.TrimEntries).ToList(),
                                BookFor = bookFor
                            };
                        })
                        .ToList();
        }
    }
}
