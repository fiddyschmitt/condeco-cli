using System.Runtime.InteropServices;

namespace condeco_cli.Scheduling
{
    public static class Scheduler
    {
        static SchedulerPlatform Platform =>
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? new WindowsScheduler() :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? new MacScheduler() :
            new LinuxScheduler();

        public static string GetConfigSlug(string configPath) =>
            Path.GetFileName(configPath);

        public static ScheduleInfo? GetSchedule(string taskType, string configSlug) =>
            Platform.GetSchedule(taskType, configSlug);

        public static void CreateOrUpdateSchedule(
            string taskType,
            string configSlug,
            DayOfWeek[]? days,
            TimeOnly time,
            string exePath,
            string configPath,
            string apiFlag)
        {
            var exeDir = Path.GetDirectoryName(exePath) ?? ".";
            var logDir = Path.Combine(exeDir, "schedules", configSlug);
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, $"{taskType}.log");

            string args;
            if (taskType == "booking")
                args = $"--autobook --wait-for-rollover 5 --api {apiFlag} --config \"{configPath}\"";
            else
                args = $"--checkin --api {apiFlag} --config \"{configPath}\"";

            Platform.CreateOrUpdate(taskType, configSlug, days, time, exePath, args, logFile);
        }

        public static void DeleteSchedule(string taskType, string configSlug) =>
            Platform.Delete(taskType, configSlug);
    }
}
