using System.Diagnostics;

namespace condeco_cli.Scheduling
{
    public abstract class SchedulerPlatform
    {
        public abstract ScheduleInfo? GetSchedule(string taskType, string configSlug);
        public abstract void CreateOrUpdate(string taskType, string configSlug, DayOfWeek[]? days, TimeOnly time, string exePath, string args, string logFile);
        public abstract void Delete(string taskType, string configSlug);

        protected static (int ExitCode, string Output) RunProcess(string fileName, string arguments)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return (-1, "Failed to start process");

                var stdout = process.StandardOutput.ReadToEnd();
                var stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                return (process.ExitCode, stdout + stderr);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        protected static bool IsDaily(DayOfWeek[]? days) =>
            days == null || days.Length == 0 || days.Length == 7;
    }
}
