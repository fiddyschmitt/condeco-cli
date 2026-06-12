using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCondeco.Extensions
{
    public static class StringExtensions
    {
        public static string ToString(this IEnumerable<string> values, string separator)
        {
            var result = string.Join(separator, values);
            return result;
        }

        public static string ReplaceInvalidChars(this string filename, string replacement)
        {
            return string.Join(replacement, filename.Split(Path.GetInvalidFileNameChars()));
        }

        //Accepts bare hostnames (e.g. "tenant.epturacloud.com") by defaulting to https://.
        //.NET's Uri / HttpClient require an absolute URI with a scheme, so a scheme-less host
        //would otherwise throw rather than be handled by the HTTP client.
        public static string NormalizeBaseUrl(this string url)
        {
            var trimmed = url.Trim();
            if (trimmed.Length > 0 && !trimmed.Contains("://"))
            {
                trimmed = "https://" + trimmed;
            }
            return trimmed;
        }

        public static string ToJson(this object? obj, bool indent = false)
        {
            var settings = new JsonSerializerSettings() { Formatting = Formatting.Indented };
            if (!indent)
            {
                settings = new JsonSerializerSettings() { Formatting = Formatting.None };
            }

            var result = JsonConvert.SerializeObject(obj, settings);
            return result;
        }

        public static string ToJson<T>(this object obj, bool indent = false)
        {
            var settings = new JsonSerializerSettings() { Formatting = Formatting.Indented };
            if (!indent)
            {
                settings = new JsonSerializerSettings() { Formatting = Formatting.None };
            }

            var result = JsonConvert.SerializeObject((T)obj, settings);
            return result;
        }

        public static T ToObject<T>(this string jsonStr)
        {
            var result = JsonConvert.DeserializeObject<T>(jsonStr)
                            ?? throw new Exception($"Could not deserialize string to {typeof(T).Name}:{Environment.NewLine}{jsonStr}");

            return result;
        }
    }
}
