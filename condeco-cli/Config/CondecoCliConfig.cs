using condeco_cli.Model;
using condeco_cli.Updating;
using libCondeco;
using libCondeco.Extensions;
using libCondeco.Model.People;
using libCondeco.Model.Space;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Config
{
    public class CondecoCliConfig
    {
        public readonly string ConfigFilename;

        public CondecoCliConfig(string ConfigFilename)
        {
            this.ConfigFilename = ConfigFilename;
            Reload();
        }

        //Exclude_Dates must mean the same dates on every machine a config file travels to.
        //DateOnly.Parse uses the machine culture, so "05/08/2026" silently meant 8-May on a
        //US-locale machine and 5-Aug on an Australian one. Accept only the documented ISO form
        //plus day-first dd/MM/yyyy, both parsed culture-invariantly.
        private static readonly string[] ExcludeDateFormats = ["yyyy-MM-dd", "dd/MM/yyyy"];

        private static DateOnly ParseExcludeDate(string dateStr)
        {
            if (DateOnly.TryParseExact(dateStr, ExcludeDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
            {
                return result;
            }

            throw new FormatException($"Invalid Exclude_Dates date: '{dateStr}'. Expected 'yyyy-MM-dd' (e.g. 2026-08-25) or 'dd/MM/yyyy'.");
        }

        public Account Account { get; private set; } = new()
        {
            BaseUrl = ""
        };

        public List<Booking> Bookings { get; private set; } = [];

        public UpdateSettings UpdateSettings { get; set; } = new();

        public void Save()
        {
            var ini = new Ini();

            ini["Account"]["BaseUrl"] = Account.BaseUrl;

            if (!string.IsNullOrEmpty(Account.Username))
            {
                ini["Account"]["Username"] = Account.Username;
                ini["Account"]["Password"] = Account.Password;
            }
            else if (!string.IsNullOrEmpty(Account.RefreshToken))
            {
                ini["Account"]["RefreshToken"] = Account.RefreshToken;
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

                    if (booking.ExcludeDates.Count > 0)
                    {
                        var excludeDatesString = booking
                                                    .ExcludeDates
                                                    .Select(range => $"{range.FromDate} - {range.ToDate}")
                                                    .ToString(", ");

                        section["Exclude_Dates"] = excludeDatesString;
                    }
                });

            ini["Updates"]["AutoUpdate"] = UpdateSettings.AutoUpdate.ToString();
            if (UpdateSettings.FailedVersions.Count > 0)
            {
                ini["Updates"]["FailedVersions"] = string.Join(",", UpdateSettings.FailedVersions);
            }

            var iniStr = ini.ToString();
            File.WriteAllText(ConfigFilename, iniStr);
        }

        public void Reload()
        {
            var ini = new Ini();

            if (File.Exists(ConfigFilename))
            {
                ini.Parse(File.ReadAllText(ConfigFilename));
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
            else
            {
                Account.RefreshToken = ini["Account"]["RefreshToken"] ?? "";
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

                            List<(DateOnly FromDate, DateOnly ToDate)> excludeDates;
                            var excludeDatesString = section["Exclude_Dates"];
                            if (string.IsNullOrEmpty(excludeDatesString))
                            {
                                excludeDates = [];
                            }
                            else
                            {
                                excludeDates = excludeDatesString
                                                .Split(",")
                                                .Select(pair =>
                                                {
                                                    var tokens = pair.Split(" - ", StringSplitOptions.None);
                                                    if (tokens.Length < 2)
                                                        throw new FormatException($"Invalid Exclude_Dates entry: '{pair}'. Expected format: 'yyyy-MM-dd - yyyy-MM-dd'.");
                                                    var fromDate = ParseExcludeDate(tokens[0].Trim());
                                                    var toDate = ParseExcludeDate(tokens[1].Trim());
                                                    return (fromDate, toDate);
                                                })
                                                .ToList();
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
                                BookFor = bookFor,
                                ExcludeDates = excludeDates
                            };
                        })
                        .ToList();

            var autoUpdateStr = ini["Updates"]["AutoUpdate"];
            var failedVersionsStr = ini["Updates"]["FailedVersions"];

            UpdateSettings = new UpdateSettings
            {
                AutoUpdate = bool.TryParse(autoUpdateStr, out var au) && au,
                FailedVersions = string.IsNullOrEmpty(failedVersionsStr)
                    ? []
                    : failedVersionsStr.Split(",", StringSplitOptions.TrimEntries).ToList()
            };
        }
    }
}
