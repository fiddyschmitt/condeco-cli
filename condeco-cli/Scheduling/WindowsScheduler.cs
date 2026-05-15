using System.Text.RegularExpressions;

namespace condeco_cli.Scheduling
{
    public class WindowsScheduler : SchedulerPlatform
    {
        static string FriendlyType(string taskType) => taskType switch
        {
            "booking" => "Book",
            "checkin" => "Check in",
            _ => taskType
        };

        static string TaskName(string taskType, string configSlug) =>
            $"condeco-cli\\{FriendlyType(taskType)} ({configSlug})";

        public override ScheduleInfo? GetSchedule(string taskType, string configSlug)
        {
            var (exitCode, output) = RunProcess("schtasks", $"/query /tn \"{TaskName(taskType, configSlug)}\" /fo LIST /v");
            if (exitCode != 0) return null;

            var timeMatch = Regex.Match(output, @"Start Time:\s+(.+)");
            var daysMatch = Regex.Match(output, @"Days:\s+(.+)");
            var schedTypeMatch = Regex.Match(output, @"Schedule Type:\s+(.+)");

            if (!timeMatch.Success) return null;

            var time = TimeOnly.Parse(timeMatch.Groups[1].Value.Trim());

            string days;
            if (schedTypeMatch.Success && schedTypeMatch.Groups[1].Value.Trim().Contains("Daily", StringComparison.OrdinalIgnoreCase))
            {
                days = "Daily";
            }
            else if (daysMatch.Success)
            {
                days = ShortenDayNames(daysMatch.Groups[1].Value.Trim());
            }
            else
            {
                days = "unknown";
            }

            return new ScheduleInfo { Days = days, Time = time };
        }

        public override void CreateOrUpdate(string taskType, string configSlug, DayOfWeek[]? days, TimeOnly time, string exePath, string args, string logFile)
        {
            var tn = TaskName(taskType, configSlug);
            var timeStr = time.ToString("HH:mm");

            var batFile = Path.ChangeExtension(logFile, ".bat");
            File.WriteAllText(batFile, $"\"{exePath}\" {args} >> \"{logFile}\" 2>&1\r\n");

            string schedArgs;
            if (IsDaily(days))
            {
                schedArgs = $"/sc daily /st {timeStr}";
            }
            else
            {
                var dayStr = string.Join(",", days!.Select(DayToSchtasks));
                schedArgs = $"/sc weekly /d {dayStr} /st {timeStr}";
            }

            var (exitCode, output) = RunProcess("schtasks", $"/create /tn \"{tn}\" /tr \"\\\"{batFile}\\\"\" {schedArgs} /f");
            if (exitCode != 0)
                throw new Exception($"schtasks /create failed (exit {exitCode}): {output}");

            DisableAcOnlyCondition(tn);
        }

        public override void Delete(string taskType, string configSlug)
        {
            var (exitCode, output) = RunProcess("schtasks", $"/delete /tn \"{TaskName(taskType, configSlug)}\" /f");
            if (exitCode != 0)
                throw new Exception($"schtasks /delete failed (exit {exitCode}): {output}");

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var batFile = Path.Combine(exeDir, "schedules", configSlug, $"{taskType}.bat");
            if (File.Exists(batFile))
                File.Delete(batFile);
        }

        static void DisableAcOnlyCondition(string taskName)
        {
            var (exitCode, xml) = RunProcess("schtasks", $"/query /tn \"{taskName}\" /xml");
            if (exitCode != 0) return;

            xml = xml.Replace("<DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries>",
                              "<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, xml);
            RunProcess("schtasks", $"/create /tn \"{taskName}\" /xml \"{tempFile}\" /f");
            File.Delete(tempFile);
        }

        static string DayToSchtasks(DayOfWeek d) => d switch
        {
            DayOfWeek.Sunday => "SUN",
            DayOfWeek.Monday => "MON",
            DayOfWeek.Tuesday => "TUE",
            DayOfWeek.Wednesday => "WED",
            DayOfWeek.Thursday => "THU",
            DayOfWeek.Friday => "FRI",
            DayOfWeek.Saturday => "SAT",
            _ => d.ToString()[..3].ToUpper()
        };

        static string ShortenDayNames(string days)
        {
            return Regex.Replace(days, @"(SUNDAY|MONDAY|TUESDAY|WEDNESDAY|THURSDAY|FRIDAY|SATURDAY)",
                m => m.Value[..3], RegexOptions.IgnoreCase);
        }
    }
}
