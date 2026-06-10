namespace condeco_cli.Updating
{
    public class UpdateSettings
    {
        public bool AutoUpdate { get; set; } = false;
        public List<string> FailedVersions { get; set; } = [];
    }
}
