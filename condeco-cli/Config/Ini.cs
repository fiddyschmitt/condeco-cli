using condeco_cli.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;
using static System.Net.Mime.MediaTypeNames;

namespace condeco_cli.Config
{
    public class Ini
    {
        public List<Section> Sections = new();

        public Ini()
        {
        }

        public Section this[string sectionName]
        {
            get
            {
                var section = Sections.FirstOrDefault(sec => sec.SectionName.Equals(sectionName, StringComparison.OrdinalIgnoreCase)) ?? new Section();
                return section;
            }
        }

        public void Parse(string iniContent)
        {
            var lines = Regex
                            .Split(iniContent, "\r\n|\r|\n")
                            .ToList();

            Section? curentSection = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("["))
                {
                    if (curentSection != null)
                    {
                        //finished with the current section
                        Sections.Add(curentSection);
                    }

                    var sectionName = line.Substring(1, line.Length - 2);

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
    }

    public class Section
    {
        public string SectionName = "";
        public List<Key> Keys = [];

        public string this[string keyName]
        {
            get
            {
                var key = Keys.FirstOrDefault(sec => sec.KeyName.Equals(keyName, StringComparison.OrdinalIgnoreCase));
                return key?.Value ?? "";
            }
        }
    }

    public class Key
    {
        public string KeyName = "";
        public string Value = "";
    }
}
