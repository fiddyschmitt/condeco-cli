namespace condeco_cli.Scheduling
{
    public class LinuxScheduler : SchedulerPlatform
    {
        static string CronMarker(string taskType, string configSlug) =>
            $"# condeco-cli:{taskType}:{configSlug}";

        public override ScheduleInfo? GetSchedule(string taskType, string configSlug)
        {
            var (exitCode, output) = RunProcess("crontab", "-l");
            if (exitCode != 0) return null;

            var marker = CronMarker(taskType, configSlug);
            var lines = output.Split('\n');
            for (int i = 0; i < lines.Length - 1; i++)
            {
                if (lines[i].Trim() == marker)
                    return ParseCronLine(lines[i + 1].Trim());
            }
            return null;
        }

        public override void CreateOrUpdate(string taskType, string configSlug, DayOfWeek[]? days, TimeOnly time, string exePath, string args, string logFile)
        {
            Delete(taskType, configSlug);

            var shFile = Path.ChangeExtension(logFile, ".sh");
            File.WriteAllText(shFile, $"#!/bin/sh\n\"{exePath}\" {args} >> \"{logFile}\" 2>&1\n");
            RunProcess("chmod", $"+x \"{shFile}\"");

            var (exitCode, existing) = RunProcess("crontab", "-l");
            var crontab = exitCode == 0 ? existing.TrimEnd() : "";

            var marker = CronMarker(taskType, configSlug);
            var dowField = IsDaily(days)
                ? "*"
                : string.Join(",", days!.Select(d => (int)d));
            var cronLine = $"{time.Minute} {time.Hour} * * {dowField} \"{shFile}\"";

            if (!string.IsNullOrEmpty(crontab))
                crontab += "\n";
            crontab += $"{marker}\n{cronLine}\n";

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, crontab);
            var (ec, output) = RunProcess("crontab", tempFile);
            File.Delete(tempFile);
            if (ec != 0)
                throw new Exception($"crontab install failed (exit {ec}): {output}");
        }

        public override void Delete(string taskType, string configSlug)
        {
            var (exitCode, existing) = RunProcess("crontab", "-l");
            if (exitCode != 0) return;

            var marker = CronMarker(taskType, configSlug);
            var lines = existing.Split('\n').ToList();
            var newLines = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() == marker && i + 1 < lines.Count)
                {
                    i++;
                    continue;
                }
                newLines.Add(lines[i]);
            }

            var tempFile = Path.GetTempFileName();
            File.WriteAllText(tempFile, string.Join("\n", newLines));
            RunProcess("crontab", tempFile);
            File.Delete(tempFile);

            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            var shFile = Path.Combine(exeDir, "schedules", configSlug, $"{taskType}.sh");
            if (File.Exists(shFile))
                File.Delete(shFile);
        }

        static ScheduleInfo? ParseCronLine(string cronLine)
        {
            var parts = cronLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 5) return null;

            var minute = int.TryParse(parts[0], out var m) ? m : 0;
            var hour = int.TryParse(parts[1], out var h) ? h : 0;
            var dowField = parts[4];

            string days;
            if (dowField == "*")
            {
                days = "Daily";
            }
            else
            {
                days = string.Join(",", dowField.Split(',').Select(d =>
                {
                    if (int.TryParse(d, out var num))
                        return ((DayOfWeek)num).ToString()[..3];
                    return d.Length >= 3 ? d[..3] : d;
                }));
            }

            return new ScheduleInfo { Days = days, Time = new TimeOnly(hour, minute) };
        }
    }
}
