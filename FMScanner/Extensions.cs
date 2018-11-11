using System;
using System.Collections.Generic;
using System.Linq;
using static System.IO.Path;
using static System.StringComparison;

namespace FMScanner
{
    internal static class Extensions
    {
        #region Queries

        internal static bool IsEnglishReadme(this string value)
        {
            var rNoExt = value.RemoveExtension();
            if (string.IsNullOrEmpty(rNoExt)) return false;

            return rNoExt.Equals("fminfo-en", OrdinalIgnoreCase) ||
                   rNoExt.Equals("fminfo-eng", OrdinalIgnoreCase) ||
                   !(rNoExt.StartsWith("fminfo", OrdinalIgnoreCase) &&
                     !rNoExt.Equals("fminfo", OrdinalIgnoreCase));
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
            return value.Contains(substring, OrdinalIgnoreCase);
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
            return value.Contains(substring, Ordinal);
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
            return first.Equals(second, OrdinalIgnoreCase);
        }

        /// <summary>
        /// Returns true if the string is "true" (case-insensitive), otherwise false.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool EqualsTrue(this string value)
        {
            return value.Equals(bool.TrueString, OrdinalIgnoreCase);
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
                   GetExtension(value).Equals(extension, OrdinalIgnoreCase);
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

            return ext.Equals(".html", OrdinalIgnoreCase) ||
                   ext.Equals(".htm", OrdinalIgnoreCase);
        }

        #region StartsWith

        private enum CaseComparison
        {
            CaseSensitive,
            CaseInsensitive,
            GivenOrUpper,
            GivenOrLower
        }

        /// <summary>
        /// StartsWith (case-insensitive). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithI(this string str, string value)
        {
            return StartsWithFastInternal(str, value, CaseComparison.CaseInsensitive);
        }

        /// <summary>
        /// StartsWith (given case or uppercase). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithGU(this string str, string value)
        {
            return StartsWithFastInternal(str, value, CaseComparison.GivenOrUpper);
        }

        /// <summary>
        /// StartsWith (given case or lowercase). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithGL(this string str, string value)
        {
            return StartsWithFastInternal(str, value, CaseComparison.GivenOrLower);
        }

        private static bool StartsWithFastInternal(this string str, string value, CaseComparison caseComparison)
        {
            if (string.IsNullOrEmpty(str) || str.Length < value.Length) return false;

            // Notes: ASCII chars are 0-127. Uppercase is 65-90; lowercase is 97-122.
            // Therefore, if a char is in one of these ranges, one can convert between cases by simply adding or
            // subtracting 32.

            for (int i = 0; i < value.Length; i++)
            {
                // Only run the slow case check if the char is non-ASCII. This also means we run it per-char
                // instead of per-string, which should make it faster, although the double ToString() and one
                // To*Invariant() hurts. How much, I dunno. I don't currently test any non-ASCII strings. We'll
                // see.
                if (value[i] > 127)
                {
                    if (value[i] != str[i] &&
                        caseComparison == CaseComparison.GivenOrUpper
                        ? !value[i].ToString().ToUpperInvariant().Equals(str[i].ToString(), Ordinal) :
                        caseComparison == CaseComparison.GivenOrLower
                        ? !value[i].ToString().ToLowerInvariant().Equals(str[i].ToString(), Ordinal)
                        : !value[i].ToString().Equals(str[i].ToString(), OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    continue;
                }

                if (str[i] >= 65 && str[i] <= 90 && value[i] >= 97 && value[i] <= 122)
                {
                    if (caseComparison == CaseComparison.GivenOrLower || str[i] != value[i] - 32) return false;
                }
                else if (value[i] >= 65 && value[i] <= 90 && str[i] >= 97 && str[i] <= 122)
                {
                    if (caseComparison == CaseComparison.GivenOrUpper || str[i] != value[i] + 32) return false;
                }
                else if (str[i] != value[i])
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

        /// <summary>
        /// Case-insensitive EndsWith.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool EndsWithI(this string str, string value)
        {
            return !string.IsNullOrEmpty(str) && str.EndsWith(value, OrdinalIgnoreCase);
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
