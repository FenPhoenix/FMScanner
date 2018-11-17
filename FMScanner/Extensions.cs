using System;
using System.Collections.Generic;
using System.Linq;
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

        #region Fast byte[] / char[] search

        // This takes about 1 second out of the entire 1098 mission scan. That makes it a hotspot, but literally
        // everything I've tried to make it faster has failed. IndexOf() calls an internal method TrySZIndexOf(),
        // which is clearly some voodoo cause it's faster than everything else in the world apparently. Also
        // multithreading? Forget it. The problem is the 1sec time is an aggregate; this takes like 5.9ms per
        // FM (5.9ms * 170 NewDark FMs = ~1sec), and setting up threads probably outweighs that, or at least
        // that's my best guess because that hasn't really worked out either. Anyway.
        internal static bool Contains(this byte[] input, byte[] pattern)
        {
            var firstByte = pattern[0];
            int index = Array.IndexOf(input, firstByte);

            while (index > -1)
            {
                for (int i = 0; i < pattern.Length; i++)
                {
                    if (index + i >= input.Length) return false;
                    if (pattern[i] != input[index + i])
                    {
                        if ((index = Array.IndexOf(input, firstByte, index + i)) == -1) return false;
                        break;
                    }

                    if (i == pattern.Length - 1) return true;
                }
            }

            return index > -1;
        }

        // Exact duplicate except for the array type, but there's nothing I can do if I want to be fast :/
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

        #endregion

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
            return value.IndexOf(substring, OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Case-insensitive Contains.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="substring"></param>
        /// <returns></returns>
        internal static bool ContainsI(this IEnumerable<string> value, string substring)
        {
            return value.Contains(substring, StringComparer.OrdinalIgnoreCase);
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

        internal static bool HasFileExtension(this string value)
        {
            return value.LastIndexOf('.') > value.LastIndexOf('/') ||
                   value.LastIndexOf('.') > value.LastIndexOf('\\') ||
                   (!value.Contains('/') && !value.Contains('\\') && value.Contains('.'));
        }

        /// <summary>
        /// Returns true if the string ends with <paramref name="extension"/> (case-insensitive).
        /// </summary>
        /// <param name="value"></param>
        /// <param name="extension"></param>
        /// <returns></returns>
        internal static bool ExtEqualsI(this string value, string extension)
        {
            if (extension[0] != '.') extension = '.' + extension;

            return value.EndsWithI(extension);
        }

        /// <summary>
        /// Returns true if value ends with ".html" or ".htm".
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool ExtIsHtml(this string value)
        {
            return value.EndsWithI(".html") || value.EndsWithI(".htm");
        }

        #region StartsWith and EndsWith

        private enum CaseComparison
        {
            CaseSensitive,
            CaseInsensitive,
            GivenOrUpper,
            GivenOrLower
        }

        private enum StartOrEnd
        {
            Start,
            End
        }

        /// <summary>
        /// StartsWith (case-insensitive). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithI(this string str, string value)
        {
            return StartsWithOrEndsWithFast(str, value, CaseComparison.CaseInsensitive, StartOrEnd.Start);
        }

        /// <summary>
        /// StartsWith (given case or uppercase). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithGU(this string str, string value)
        {
            return StartsWithOrEndsWithFast(str, value, CaseComparison.GivenOrUpper, StartOrEnd.Start);
        }

        /// <summary>
        /// StartsWith (given case or lowercase). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool StartsWithGL(this string str, string value)
        {
            return StartsWithOrEndsWithFast(str, value, CaseComparison.GivenOrLower, StartOrEnd.Start);
        }

        /// <summary>
        /// EndsWith (case-insensitive). Uses a fast ASCII compare where possible.
        /// </summary>
        /// <param name="str"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        internal static bool EndsWithI(this string str, string value)
        {
            return StartsWithOrEndsWithFast(str, value, CaseComparison.CaseInsensitive, StartOrEnd.End);
        }

        private static bool StartsWithOrEndsWithFast(this string str, string value,
            CaseComparison caseComparison, StartOrEnd startOrEnd)
        {
            if (string.IsNullOrEmpty(str) || str.Length < value.Length) return false;

            // Notes: ASCII chars are 0-127. Uppercase is 65-90; lowercase is 97-122.
            // Therefore, if a char is in one of these ranges, one can convert between cases by simply adding or
            // subtracting 32.

            var start = startOrEnd == StartOrEnd.Start;
            var siStart = start ? 0 : str.Length - value.Length;
            var siEnd = start ? value.Length : str.Length;

            for (int si = siStart, vi = 0; si < siEnd; si++, vi++)
            {
                // Only run the slow case check if the char is non-ASCII. This also means we run it per-char
                // instead of per-string, which should make it faster, although the double ToString() and one
                // To*Invariant() hurts. How much, I dunno. I don't currently test any non-ASCII strings. We'll
                // see.
                if (value[vi] > 127)
                {
                    if (value[vi] != str[si] &&
                        caseComparison == CaseComparison.GivenOrUpper
                        ? !value[vi].ToString().ToUpperInvariant().Equals(str[si].ToString(), Ordinal) :
                        caseComparison == CaseComparison.GivenOrLower
                        ? !value[vi].ToString().ToLowerInvariant().Equals(str[si].ToString(), Ordinal)
                        : !value[vi].ToString().Equals(str[si].ToString(), OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    continue;
                }

                if (str[si] >= 65 && str[si] <= 90 && value[vi] >= 97 && value[vi] <= 122)
                {
                    if (caseComparison == CaseComparison.GivenOrLower || str[si] != value[vi] - 32) return false;
                }
                else if (value[vi] >= 65 && value[vi] <= 90 && str[si] >= 97 && str[si] <= 122)
                {
                    if (caseComparison == CaseComparison.GivenOrUpper || str[si] != value[vi] + 32) return false;
                }
                else if (str[si] != value[vi])
                {
                    return false;
                }
            }

            return true;
        }

        #endregion

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
