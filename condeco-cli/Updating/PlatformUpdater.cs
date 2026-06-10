using System.Runtime.InteropServices;

namespace condeco_cli.Updating
{
    public abstract class PlatformUpdater
    {
        public abstract string AssetName { get; }

        public abstract void SetExecutablePermission(string path);

        //For filesystems where the running executable can't be touched at all (eg. NTFS mounted in Linux),
        //spawn a detached helper which waits for this process to exit and then performs the swap
        public virtual bool CanSwapOnExit => false;

        public virtual void ScheduleSwapOnExit(string downloadedPath, string currentPath)
        {
            throw new PlatformNotSupportedException();
        }

        //Rename rather than overwrite, because a running executable can be renamed but not overwritten on Windows
        //(including Windows-backed mounts such as WSL drvfs)
        public virtual void SwapExecutable(string downloadedPath, string currentPath)
        {
            var oldPath = currentPath + ".old";
            File.Move(currentPath, oldPath);

            try
            {
                File.Move(downloadedPath, currentPath);
            }
            catch
            {
                //Roll back, so the user isn't left without an executable
                File.Move(oldPath, currentPath);
                throw;
            }
        }

        //Only removes files this updater created, to avoid deleting unrelated user files (eg. config.ini.old)
        public virtual void CleanupOldFiles(string exePath)
        {
            TryDelete(exePath + ".old");

            var exeDir = Path.GetDirectoryName(exePath);
            if (exeDir != null)
            {
                TryDelete(Path.Combine(exeDir, AssetName + ".update"));
            }
        }

        public static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        public static PlatformUpdater? Create()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return new WindowsUpdater();
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var arch = RuntimeInformation.OSArchitecture;
                if (arch == Architecture.Arm64)
                {
                    return new LinuxUpdater("condeco-cli-linux-arm64");
                }
                if (arch == Architecture.Arm)
                {
                    return new LinuxUpdater("condeco-cli-linux-arm");
                }
                return new LinuxUpdater("condeco-cli-linux-x64");
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return new LinuxUpdater("condeco-cli-osx-x64");
            }

            return null;
        }
    }
}
