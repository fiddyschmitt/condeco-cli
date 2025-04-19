using condeco_cli.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace condeco_cli.Config
{
    public partial class Ini
    {
        public List<Section> Sections = [];

        public Ini()
        {
        }

        public Section this[string sectionName]
        {
            get
            {
                var section = Sections.FirstOrDefault(sec => sec.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase));
                if (section == null)
                {
                    section = new Section()
                    {
                        SectionName = sectionName
                    };

                    Sections.Add(section);
                }
                return section;
            }
        }

        public override string ToString()
        {
            var result = Sections
                            .Select(section => section.ToString())
                            .ToString(Environment.NewLine + Environment.NewLine);

            return result;
        }

        public void Parse(string iniContent)
        {
            var lines = CarriageReturnRegex()
                            .Split(iniContent)
                            .ToList();

            Section? curentSection = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith('['))
                {
                    if (curentSection != null)
                    {
                        //finished with the current section
                        Sections.Add(curentSection);
                    }

                    var sectionName = line[1..^1];

                    curentSection = new Section()
                    {
                        SectionName = sectionName
                    };
                }
                else
                {
                    var tokens = line.Split("=", StringSplitOptions.None);
                    if (tokens.Length >= 2)
                    {
                        var key = new Key()
                        {
                            KeyName = tokens[0],
                            Value = tokens.Skip(1).ToString("=")
                        };

                        curentSection?.Keys.Add(key);
                    }
                }
            }

            if (curentSection != null && !Sections.Contains(curentSection))
            {
                Sections.Add(curentSection);
            }
        }

        [GeneratedRegex("\r\n|\r|\n")]
        public static partial Regex CarriageReturnRegex();
    }

    public class Section
    {
        public string SectionName = "";
        public List<Key> Keys = [];

        public Key GetKey(string keyName)
        {
            var key = Keys.FirstOrDefault(sec => sec.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
            if (key == null)
            {
                key = new Key()
                {
                    KeyName = keyName,
                    Value = ""
                };

                Keys.Add(key);
            }

            return key;
        }

        public string this[string keyName]
        {
            get
            {
                var key = GetKey(keyName);

                return key.Value;
            }
            set
            {
                Keys.Add(new Key()
                {
                    KeyName = keyName,
                    Value = value
                });
            }
        }

        public override string ToString()
        {
            var keysString = Keys
                                .Select(key => key.ToString())
                                .ToString(Environment.NewLine);

            var result = $"[{SectionName}]{Environment.NewLine}{keysString}";
            return result;
        }
    }

    public class Key
    {
        public string KeyName = "";
        public string Value = "";

        public override string ToString()
        {
            var result = $"{KeyName}={Value}";
            return result;
        }
    }
}
