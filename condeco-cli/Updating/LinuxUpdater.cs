using System.Diagnostics;

namespace condeco_cli.Updating
{
    public class LinuxUpdater : PlatformUpdater
    {
        private readonly string assetName;

        public LinuxUpdater(string assetName)
        {
            this.assetName = assetName;
        }

        public override string AssetName => assetName;

        public override void SetExecutablePermission(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            var mode = File.GetUnixFileMode(path)
                        | UnixFileMode.UserExecute
                        | UnixFileMode.GroupExecute
                        | UnixFileMode.OtherExecute;

            File.SetUnixFileMode(path, mode);
        }

        public override bool CanSwapOnExit => true;

        public override void ScheduleSwapOnExit(string downloadedPath, string currentPath)
        {
            //The pid and paths are passed as positional arguments rather than interpolated into the script,
            //so that shell metacharacters in the paths can't break or inject into the command
            var script = "while kill -0 \"$1\" 2>/dev/null; do sleep 0.2; done; mv -f \"$2\" \"$3\"";

            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
            startInfo.ArgumentList.Add("sh");
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(downloadedPath);
            startInfo.ArgumentList.Add(currentPath);

            _ = Process.Start(startInfo) ?? throw new Exception("Could not start the deferred update helper.");
        }
    }
}
