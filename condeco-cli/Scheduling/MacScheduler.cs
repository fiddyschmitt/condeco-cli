using System.Xml.Linq;

namespace condeco_cli.Scheduling
{
    public class MacScheduler : SchedulerPlatform
    {
        static string PlistLabel(string taskType, string configSlug) =>
            $"com.condeco-cli.{taskType}.{configSlug}";

        static string PlistPath(string taskType, string configSlug) =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "LaunchAgents", $"{PlistLabel(taskType, configSlug)}.plist");

        public override ScheduleInfo? GetSchedule(string taskType, string configSlug)
        {
            var path = PlistPath(taskType, configSlug);
            if (!File.Exists(path)) return null;

            try
            {
                var doc = XDocument.Load(path);
                var dict = doc.Root?.Element("dict");
                if (dict == null) return null;

                var elements = dict.Elements().ToList();
                for (int i = 0; i < elements.Count - 1; i++)
                {
                    if (elements[i].Name == "key" && elements[i].Value == "StartCalendarInterval")
                    {
                        var next = elements[i + 1];
                        if (next.Name == "dict")
                            return ParseCalendarDict(next);
                        if (next.Name == "array")
                            return ParseCalendarArray(next);
                    }
                }
            }
            catch { }
            return null;
        }

        public override void CreateOrUpdate(string taskType, string configSlug, DayOfWeek[]? days, TimeOnly time, string exePath, string args, string logFile)
        {
            Delete(taskType, configSlug);

            var label = PlistLabel(taskType, configSlug);
            var path = PlistPath(taskType, configSlug);

            XElement calendarInterval;
            if (IsDaily(days))
            {
                calendarInterval = PlistDict(("Hour", time.Hour), ("Minute", time.Minute));
            }
            else if (days!.Length == 1)
            {
                calendarInterval = PlistDict(("Hour", time.Hour), ("Minute", time.Minute), ("Weekday", (int)days[0]));
            }
            else
            {
                calendarInterval = new XElement("array",
                    days.Select(d => PlistDict(("Hour", time.Hour), ("Minute", time.Minute), ("Weekday", (int)d))));
            }

            var programArgs = new XElement("array",
                new XElement("string", exePath),
                SplitArgs(args).Select(a => new XElement("string", a)));

            var plist = new XDocument(
                new XDeclaration("1.0", "UTF-8", null),
                new XElement("plist", new XAttribute("version", "1.0"),
                    new XElement("dict",
                        new XElement("key", "Label"), new XElement("string", label),
                        new XElement("key", "ProgramArguments"), programArgs,
                        new XElement("key", "StartCalendarInterval"), calendarInterval,
                        new XElement("key", "StandardOutPath"), new XElement("string", logFile),
                        new XElement("key", "StandardErrorPath"), new XElement("string", logFile))));

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            plist.Save(path);

            RunProcess("launchctl", $"load \"{path}\"");
        }

        static XElement PlistDict(params (string Key, int Value)[] entries) =>
            new("dict", entries.SelectMany(e => new XElement[] {
                new("key", e.Key),
                new("integer", e.Value)
            }));

        public override void Delete(string taskType, string configSlug)
        {
            var path = PlistPath(taskType, configSlug);
            if (!File.Exists(path)) return;

            RunProcess("launchctl", $"unload \"{path}\"");
            File.Delete(path);
        }

        static List<string> SplitArgs(string input)
        {
            var result = new List<string>();
            var current = new System.Text.StringBuilder();
            bool inQuotes = false;
            foreach (var c in input)
            {
                if (c == '"') { inQuotes = !inQuotes; continue; }
                if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                    continue;
                }
                current.Append(c);
            }
            if (current.Length > 0) result.Add(current.ToString());
            return result;
        }

        static ScheduleInfo ParseCalendarDict(XElement dict)
        {
            var hour = 0;
            var minute = 0;
            var weekday = -1;
            var entries = dict.Elements().ToList();
            for (int i = 0; i < entries.Count - 1; i++)
            {
                if (entries[i].Name != "key") continue;
                var val = int.Parse(entries[i + 1].Value);
                switch (entries[i].Value)
                {
                    case "Hour": hour = val; break;
                    case "Minute": minute = val; break;
                    case "Weekday": weekday = val; break;
                }
            }

            var days = weekday >= 0 ? ((DayOfWeek)weekday).ToString()[..3] : "Daily";
            return new ScheduleInfo { Days = days, Time = new TimeOnly(hour, minute) };
        }

        static ScheduleInfo ParseCalendarArray(XElement array)
        {
            var daysList = new List<string>();
            TimeOnly time = default;

            foreach (var dict in array.Elements("dict"))
            {
                var info = ParseCalendarDict(dict);
                time = info.Time;
                daysList.Add(info.Days);
            }

            var days = daysList.Count == 7 ? "Daily" : string.Join(",", daysList.Distinct());
            return new ScheduleInfo { Days = days, Time = time };
        }
    }
}
