namespace condeco_cli.Updating
{
    public class UpdateChecker
    {
        public static bool IsNewerVersion(string currentVersion, Version latestVersion)
        {
            if (Version.TryParse(currentVersion, out var current))
            {
                return latestVersion > current;
            }

            return false;
        }

        public static bool IsVersionBlocked(Version version, List<string> failedVersions)
        {
            return failedVersions.Contains(version.ToString());
        }

        public static (UpdateOutcome Outcome, string Message) DownloadAndInstall(
            HttpClient httpClient, GitHubRelease release, PlatformUpdater updater)
        {
            var currentExePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExePath))
            {
                return (UpdateOutcome.Skipped, "Could not determine the current executable path.");
            }

            var exeDir = Path.GetDirectoryName(currentExePath);
            if (string.IsNullOrEmpty(exeDir))
            {
                return (UpdateOutcome.Skipped, "Could not determine the executable directory.");
            }

            var lockPath = Path.Combine(exeDir, UpdateLock.LockFileName);
            using var updateLock = UpdateLock.TryAcquire(lockPath);
            if (updateLock == null)
            {
                return (UpdateOutcome.Skipped, "Another update is already in progress.");
            }

            updater.CleanupOldFiles(currentExePath);

            var asset = release.Assets.Find(a => a.Name.Equals(updater.AssetName, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
            {
                return (UpdateOutcome.Failed, $"Could not find asset '{updater.AssetName}' in the release.");
            }

            var updateFilePath = Path.Combine(exeDir, updater.AssetName + ".update");

            //Download failures are treated as transient (Skipped), so the version isn't permanently blocked
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
                request.Headers.Add("User-Agent", $"condeco-cli/{Program.PROGRAM_VERSION}");

                var response = httpClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    return (UpdateOutcome.Skipped, $"Download failed with status {response.StatusCode}.");
                }

                using var fileStream = File.Create(updateFilePath);
                response.Content.ReadAsStream().CopyTo(fileStream);
            }
            catch (Exception ex)
            {
                PlatformUpdater.TryDelete(updateFilePath);
                return (UpdateOutcome.Skipped, $"Download failed: {ex.Message}");
            }

            var versionStr = release.TagName.TrimStart('v');

            try
            {
                updater.SwapExecutable(updateFilePath, currentExePath);
                updater.SetExecutablePermission(currentExePath);

                return (UpdateOutcome.Success, $"Updated to {versionStr}. Restart to use the new version.");
            }
            catch (Exception ex) when (updater.CanSwapOnExit && ex is IOException or UnauthorizedAccessException)
            {
                //The running executable is locked (eg. NTFS mounted in Linux), so apply the update after this process exits
                try
                {
                    updater.SetExecutablePermission(updateFilePath);
                    updater.ScheduleSwapOnExit(updateFilePath, currentExePath);

                    return (UpdateOutcome.Success, $"Downloaded {versionStr}. It will be applied when condeco-cli exits.");
                }
                catch (Exception deferEx)
                {
                    PlatformUpdater.TryDelete(updateFilePath);
                    return (UpdateOutcome.Failed, $"Update failed: {deferEx.Message}");
                }
            }
            catch (Exception ex)
            {
                PlatformUpdater.TryDelete(updateFilePath);
                return (UpdateOutcome.Failed, $"Update failed: {ex.Message}");
            }
        }
    }

    public enum UpdateOutcome
    {
        Success,
        Skipped,
        Failed
    }
}
