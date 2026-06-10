namespace condeco_cli.Updating
{
    public class UpdateLock : IDisposable
    {
        public const string LockFileName = ".condeco-cli-update.lock";

        private readonly FileStream stream;
        private readonly string lockPath;

        private UpdateLock(FileStream stream, string lockPath)
        {
            this.stream = stream;
            this.lockPath = lockPath;
        }

        public static UpdateLock? TryAcquire(string lockPath)
        {
            try
            {
                var stream = new FileStream(lockPath, FileMode.Create, FileAccess.Write, FileShare.None);
                var pidBytes = System.Text.Encoding.UTF8.GetBytes(Environment.ProcessId.ToString());
                stream.Write(pidBytes);
                stream.Flush();
                return new UpdateLock(stream, lockPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Dispose()
        {
            stream.Dispose();
            try
            {
                File.Delete(lockPath);
            }
            catch
            {
            }

            GC.SuppressFinalize(this);
        }
    }
}
