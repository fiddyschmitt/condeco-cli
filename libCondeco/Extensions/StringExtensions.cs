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
    }
}
