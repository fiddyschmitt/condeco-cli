﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace condeco_cli.Extensions
{
    public static class StringExtensions
    {
        public static string ToString(this IEnumerable<string> values, string separator)
        {
            var result = string.Join(separator, values);
            return result;
        }

        public static string Pluralize(this string singular, int count)
        {
            if (count == 1)
            {
                return singular;
            }

            return $"{singular}s";
        }
    }
}
