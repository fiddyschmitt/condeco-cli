namespace condeco_cli.Updating
{
    public class WindowsUpdater : PlatformUpdater
    {
        public override string AssetName => "condeco-cli-win-x64.exe";

        public override void SetExecutablePermission(string path)
        {
        }
    }
}
