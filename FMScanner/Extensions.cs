﻿using System;
using System.Collections.Generic;
using System.Linq;
using static System.IO.Path;

namespace FMScanner
{
    internal static class Extensions
    {
        #region Queries

        internal static bool IsEnglishReadme(this string value)
        {
            var rNoExt = value.RemoveExtension();
            if (string.IsNullOrEmpty(rNoExt)) return false;

            return rNoExt.Equals("fminfo-en", StringComparison.OrdinalIgnoreCase) ||
                   rNoExt.Equals("fminfo-eng", StringComparison.OrdinalIgnoreCase) ||
                   !(rNoExt.StartsWith("fminfo", StringComparison.OrdinalIgnoreCase) &&
                     !rNoExt.Equals("fminfo", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the number of times a character appears in a string.
        /// Avoids whatever silly overhead junk Count(predicate) is doing.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="character"></param>
        /// <returns></returns>
        internal static int CountChars(this string value, char character)
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++) if (value[i] == character) count++;

            return count;
        }

        internal static bool Contains(this char[] input, char[] pattern)
        {
            var firstChar = pattern[0];
            int index = Array.IndexOf(input, firstChar);

            while (index > -1)
            {
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (index + i >= input.Length) return false;
                    if (pattern[i] != input[index + i])
                    {
                        if ((index = Array.IndexOf(input, firstChar, index + i)) == -1) return false;
                        break;
                    }

                    if (i == pattern.Length - 1) return true;
                }
            }

            return index > -1;
        }

        internal static bool Contains(this string value, string substring, StringComparison comparison)
        {
            return value.IndexOf(substring, comparison) >= 0;
        }

        internal static bool Contains(this string value, char character)
        {
            return value.IndexOf(character) >= 0;
        }

        /// <summary>
        /// Case-insensitive Contains.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static bool ContainsI(this string value, string substring)
        {
            return value.Contains(substring, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-insensitive Contains.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="stringToSearchFor"></param>
        /// <returns></returns>
        internal static bool ContainsI(this IEnumerable<string> value, string stringToSearchFor)
        {
            return value.Contains(stringToSearchFor, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-sensitive Contains.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static bool ContainsS(this string value, string substring)
        {
            return value.Contains(substring, StringComparison.Ordinal);
        }

        /// <summary>
        /// Case-sensitive Contains.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="stringToSearchFor"></param>
        /// <returns></returns>
        internal static bool ContainsS(this IEnumerable<string> value, string stringToSearchFor)
        {
            return value.Contains(stringToSearchFor, StringComparer.Ordinal);
        }

        /// <summary>
        /// Case-insensitive Equals.
        /// </summary>
        /// <param name="first"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        internal static bool EqualsI(this string first, string second)
        {
            return first.Equals(second, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the string is "true" (case-insensitive), otherwise false.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool EqualsTrue(this string value)
        {
            return value.Equals(bool.TrueString, StringComparison.OrdinalIgnoreCase);
        }

        internal static bool HasFileExtension(this string value)
        {
            return value.LastIndexOf('.') > value.LastIndexOf('/') ||
                   value.LastIndexOf('.') > value.LastIndexOf('\\') ||
                   (!value.Contains('/') && !value.Contains('\\') && value.Contains('.'));
        }

        /// <summary>
        /// Returns true if the string ends with extension (case-insensitive).
        /// </summary>
        /// <param name="value"></param>
        /// <param name="extension"></param>
        /// <returns></returns>
        internal static bool ExtEqualsI(this string value, string extension)
        {
            if (extension[0] != '.') extension = '.' + extension;

            return !string.IsNullOrEmpty(value) &&
                   GetExtension(value).Equals(extension, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if value ends with ".html" or ".htm".
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool ExtIsHtml(this string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            var ext = GetExtension(value);

            return ext.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".htm", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-insensitive StartsWith.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithI(this string str, string value)
        {
            return !string.IsNullOrEmpty(str) && str.StartsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Case-insensitive EndsWith.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool EndsWithI(this string str, string value)
        {
            return !string.IsNullOrEmpty(str) && str.EndsWith(value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// string.IsNullOrEmpty(str) but with less typing.
        /// </summary>
        /// <param name="str"></param>
        /// <returns></returns>
        internal static bool IsEmpty(this string str)
        {
            return string.IsNullOrEmpty(str);
        }

        #endregion

        #region Modifications

        /// <summary>
        /// Removes all matching pairs of parentheses that surround the entire string, while leaving
        /// non-surrounding parentheses intact.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static string RemoveSurroundingParentheses(this string value)
        {
            if (value[0] != '(' || value[value.Length - 1] != ')') return value;

            bool surroundedByParens = false;
            do
            {
                var stack = new Stack<int>();
                for (int i = 0; i < value.Length; i++)
                {
                    switch (value[i])
                    {
                        case '(':
                            stack.Push(i);
                            surroundedByParens = false;
                            break;
                        case ')':
                            int index = stack.Any() ? stack.Pop() : -1;
                            surroundedByParens = index == 0;
                            break;
                        default:
                            surroundedByParens = false;
                            break;
                    }
                }

                if (surroundedByParens) value = value.Substring(1, value.Length - 2);
            } while (surroundedByParens);

            return value;
        }

        /// <summary>
        /// Just removes the extension from a filename, without the rather large overhead of
        /// Path.GetFileNameWithoutExtension().
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        internal static string RemoveExtension(this string fileName)
        {
            if (fileName == null) return null;
            int i = fileName.LastIndexOf('.');
            return i > -1 && i > fileName.LastIndexOf('\\') && i > fileName.LastIndexOf('/')
                ? fileName.Substring(0, i)
                : fileName;
        }

        #endregion
    }
}
