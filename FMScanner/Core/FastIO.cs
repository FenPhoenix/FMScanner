/*
FMScanner - A fast, thorough, accurate scanner for Thief 1 and Thief 2 fan missions.

Written in 2017-2018 by FenPhoenix.

To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
to this software to the public domain worldwide. This software is distributed without any warranty.

You should have received a copy of the CC0 Public Domain Dedication along with this software.
If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.
*/

using System;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace FMScanner
{
    internal static class FastIO
    {
        [SuppressMessage("ReSharper", "InconsistentNaming")]
        [SuppressMessage("ReSharper", "MemberCanBePrivate.Local")]
        [SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            internal uint dwFileAttributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            internal uint nFileSizeHigh;
            internal uint nFileSizeLow;
            internal uint dwReserved0;
            internal uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            internal string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            internal string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool FindNextFileW(IntPtr hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FindClose(IntPtr hFindFile);

        private enum FastIOSearchOption
        {
            TopDirectoryOnly,
            AllDirectories,
            AllDirectoriesSkipTop
        }

        internal static bool FilesExistSearchTop(string path, params string[] searchPatterns)
        {
            return FirstFileExists(FastIOSearchOption.TopDirectoryOnly, path, searchPatterns);
        }
        internal static bool FilesExistSearchAll(string path, params string[] searchPatterns)
        {
            return FirstFileExists(FastIOSearchOption.AllDirectories, path, searchPatterns);
        }

        internal static bool FilesExistSearchAllSkipTop(string path, params string[] searchPatterns)
        {
            return FirstFileExists(FastIOSearchOption.AllDirectoriesSkipTop, path, searchPatterns);
        }

        [SuppressMessage("ReSharper", "InconsistentNaming")]
        private static bool FirstFileExists(FastIOSearchOption searchOption, string path, params string[] searchPatterns)
        {
            var dsc = Path.DirectorySeparatorChar;

            path = path.TrimEnd(dsc);

            if (string.IsNullOrWhiteSpace(path) || Path.GetInvalidPathChars().Any(path.Contains))
            {
                throw new ArgumentException("The path '" + path + "' is invalid in some, or other, regard.");
            }

            var INVALID_HANDLE_VALUE = new IntPtr(-1);
            const int fileAttributeDirectory = 0x10;

            const int ERROR_FILE_NOT_FOUND = 0x2;

            // Other relevant errors (though we don't use them specifically at the moment)
            //const int ERROR_PATH_NOT_FOUND = 0x3;
            //const int ERROR_REM_NOT_LIST = 0x33;
            //const int ERROR_BAD_NETPATH = 0x35;

            IntPtr findHandle;
            WIN32_FIND_DATAW findData;

            // Search the base directory first, and only then search subdirectories.
            // TODO: Fix goofy duplicate code

            if (searchOption != FastIOSearchOption.AllDirectoriesSkipTop)
            {
                foreach (var p in searchPatterns)
                {
                    findHandle = FindFirstFileW(@"\\?\" + path.TrimEnd(dsc) + @"\" + p, out findData);
                    if (findHandle == INVALID_HANDLE_VALUE)
                    {
                        var err = Marshal.GetLastWin32Error();
                        if (err == ERROR_FILE_NOT_FOUND) continue;

                        // Since the framework isn't here to save us, we should blanket-catch and throw on every
                        // possible error other than file-not-found (as that's an intended scenario, obviously).
                        // This isn't as nice as you'd get from a framework method call, but it gets the job done.
                        FindClose(findHandle);
                        var ex = new Win32Exception(err);
                        throw new Win32Exception(err,
                            "System error code: " + err + "\r\n" +
                            ex.Message + "\r\n" +
                            "path: '" + path + "'\r\n" +
                            "searchPattern: '" + searchPatterns + "'");
                    }
                    do
                    {
                        if ((findData.dwFileAttributes & fileAttributeDirectory) != fileAttributeDirectory &&
                            findData.cFileName != "." && findData.cFileName != "..")
                        {
                            FindClose(findHandle);
                            return true;
                        }
                    } while (FindNextFileW(findHandle, out findData));
                    FindClose(findHandle);

                    if (searchOption == FastIOSearchOption.TopDirectoryOnly) return false;
                }
            }

            findHandle = FindFirstFileW(@"\\?\" + path.TrimEnd(dsc) + @"\*", out findData);
            if (findHandle == INVALID_HANDLE_VALUE)
            {
                var err = Marshal.GetLastWin32Error();
                if (err != ERROR_FILE_NOT_FOUND)
                {
                    FindClose(findHandle);
                    var ex = new Win32Exception(err);
                    throw new Win32Exception(err,
                        "System error code: " + err + "\r\n" +
                        ex.Message + "\r\n" +
                        "path: '" + path + "'\r\n" +
                        "searchPattern: '" + searchPatterns + "'");
                }
            }
            do
            {
                if ((findData.dwFileAttributes & fileAttributeDirectory) == fileAttributeDirectory &&
                    findData.cFileName != "." && findData.cFileName != ".." &&
                    FirstFileExists(FastIOSearchOption.AllDirectories, Path.Combine(path, findData.cFileName),
                        searchPatterns))
                {
                    FindClose(findHandle);
                    return true;
                }
            } while (FindNextFileW(findHandle, out findData));
            FindClose(findHandle);

            return false;
        }
    }
}