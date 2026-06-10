using System.Text.Json;
using System.Text.Json.Serialization;

namespace condeco_cli.Updating
{
    public class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = [];

        public Version? GetVersion()
        {
            var versionStr = TagName.TrimStart('v');
            if (Version.TryParse(versionStr, out var version))
            {
                return version;
            }

            return null;
        }

        public static (GitHubRelease? Release, string? Error) FetchLatest(HttpClient httpClient)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/repos/fiddyschmitt/condeco-cli/releases/latest");
                request.Headers.Add("User-Agent", $"condeco-cli/{Program.PROGRAM_VERSION}");

                var response = httpClient.Send(request);
                if (!response.IsSuccessStatusCode)
                {
                    return (null, $"GitHub returned {(int)response.StatusCode} ({response.StatusCode}).");
                }

                var responseStream = response.Content.ReadAsStream();
                var release = JsonSerializer.Deserialize(responseStream, GitHubReleaseJsonContext.Default.GitHubRelease);
                if (release == null)
                {
                    return (null, "Could not parse the response from GitHub.");
                }

                return (release, null);
            }
            catch (Exception ex)
            {
                var innermost = ex;
                while (innermost.InnerException != null)
                {
                    innermost = innermost.InnerException;
                }

                return (null, innermost.Message);
            }
        }
    }

    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }

    //Source-generated serializer, because reflection-based serialization is unavailable in the trimmed builds
    [JsonSerializable(typeof(GitHubRelease))]
    internal partial class GitHubReleaseJsonContext : JsonSerializerContext
    {
    }
}
