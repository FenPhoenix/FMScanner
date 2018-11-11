using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace FMScanner
{
    internal static class FMDirs
    {
        internal const string Books = "books";
        internal const string Fam = "fam";
        internal const string Intrface = "intrface";
        internal const string Mesh = "mesh";
        internal const string Motions = "motions";
        internal const string Movies = "movies";
        internal const string Obj = "obj";
        internal const string Scripts = "scripts";
        internal const string Snd = "snd";
        internal const string Strings = "strings";
        internal const string Subtitles = "subtitles";
    }

    internal static class FMFiles
    {
        internal const string MissFlag = "missflag.str";
        internal const string TitlesStr = "titles.str";
        internal const string TitleStr = "title.str";
        internal const string NewGameStr = "newgame.str";

        // Telliamed's fminfo.xml file, used in a grand total of three missions
        internal const string FMInfoXml = "fminfo.xml";

        // fm.ini, a NewDark (or just FMSel?) file
        internal const string FMIni = "fm.ini";
    }

    // Used for stripping RTF files of embedded images before scanning (performance and memory optimization)
    internal static class RtfTags
    {
        internal static readonly byte[] shppictB = Encoding.ASCII.GetBytes(@"\*\shppict");
        internal static readonly byte[] objDatatB = Encoding.ASCII.GetBytes(@"\*\objdata");
        internal static readonly byte[] nonshppictB = Encoding.ASCII.GetBytes(@"\nonshppict");
        internal static readonly byte[] pictB = Encoding.ASCII.GetBytes(@"\pict");
        internal static readonly byte[] Bytes11 = new byte[11];
        internal static readonly byte[] Bytes10 = new byte[10];
        internal static readonly byte[] Bytes5 = new byte[5];
    }

    internal static class FMConstants
    {
        // Ordered by number of actual total occurrences across all FMs:
        // gif: 153,294
        // pcx: 74,786
        // tga: 12,622
        // dds: 11,647
        // png: 11,290
        // bmp: 657
        internal static string[] ImageFileExtensions { get; } = { ".gif", ".pcx", ".tga", ".dds", ".png", ".bmp" };
        internal static string[] ImageFilePatterns { get; } = { "*.gif", "*.pcx", "*.tga", "*.dds", "*.png", "*.bmp" };

        internal static string[] MotionFilePatterns { get; } = { "*.mc", "*.mi" };
        internal static string[] MotionFileExtensions { get; } = { ".mc", ".mi" };

        // .osm for the classic scripts; .nut for Squirrel scripts for NewDark >= 1.25
        internal static string[] ScriptFileExtensions { get; } = { ".osm", ".nut" };

        internal static string[] LanguageDirs { get; } = { FMDirs.Books, FMDirs.Intrface, FMDirs.Strings };

        internal static string[] Languages { get; } =
        {
            "english", "french", "german", "russian", "italian", "spanish", "polish", "hungarian", "dutch",
            "czech"
        };

        internal static string[] DateFormats { get; } =
        {
            "yyyy/MM/dd",
            "yyyy-MM-dd",

            "MMM d yyyy",
            "MMM d, yyyy",
            "MMM dd yyyy",
            "MMM dd, yyyy",

            "MMMM d yyyy",
            "MMMM d, yyyy",
            "MMMM dd yyyy",
            "MMMM dd, yyyy",

            "d MMM yyyy",
            "d MMM, yyyy",
            "dd MMM yyyy",
            "dd MMM, yyyy"

            // Don't even bother with the nasty MM/dd/yyyy / dd/MM/yyyy stuff unless I find a lot of missions are
            // using it
        };
    }

    internal static class MisFileStrings
    {
        internal static char[] SkyObjVar = { 'S', 'K', 'Y', 'O', 'B', 'J', 'V', 'A', 'R' };
        internal static char[] RopeyArrow = { 'R', 'o', 'p', 'e', 'y', 'A', 'r', 'r', 'o', 'w' };
        internal static char[] ObjMap = { 'O', 'B', 'J', '_', 'M', 'A', 'P' };
    }

    [SuppressMessage("ReSharper", "UnusedAutoPropertyAccessor.Global")]
    internal sealed class FMIniData
    {
        public string NiceName { get; set; }
        public string ReleaseDate { get; set; }
        public string InfoFile { get; set; }
        public string Tags { get; set; }
        public string Descr { get; set; }
    }

    internal static class Regexes
    {
        internal static Regex VersionExclude1Regex =
            new Regex(@"\d\.\d+\+", RegexOptions.Compiled);

        internal static Regex TitleAnyConsecutiveLettersRegex =
            new Regex(@"\w\w", RegexOptions.Compiled);

        // TODO: [a-z] is only ASCII letters, so it won't catch lowercase other stuff I guess
        internal static Regex TitleContainsLowerCaseCharsRegex =
            new Regex(@"[a-z]", RegexOptions.Compiled);

        internal static Regex AuthorEmailRegex =
            new Regex(@"\(?\S+@\S+\.\S{2,5}\)?", RegexOptions.Compiled);

        // This doesn't need to be a regex really, but it takes like 5.4 microseconds per FM, so, yeah
        internal static Regex NewGameStrTitleRegex =
            new Regex(@"^skip_training\:\s*""(?<Title>.+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // TODO: This one looks iffy though
        internal static Regex VersionFirstNumberRegex =
            new Regex(@"[0123456789\.]+", RegexOptions.Compiled);

        // Much, much faster to iterate through possible regex matches, common ones first
        // TODO: These are still kinda slow comparatively. Profile to see if any are bottlenecks
        internal static Regex[] NewDarkVersionRegexes { get; } =
        {
            new Regex(@"NewDark (?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"(New ?Dark|""New ?Dark"").? v?(\.| )?(?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"(New ?Dark|""New ?Dark"").? .?(Version|Patch) .?(?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"(Dark ?Engine) (Version.?|v)?(\.| )?(?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(
                @"((?<!(Love |Being |Penitent |Counter-|Requiem for a |Space ))Thief|(?<!Being )Thief ?2|Thief ?II|The Metal Age) v?(\.| )?(?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(
                @"\D(?<Version>\d\.\d+) (version of |.?)New ?Dark(?! ?\d\.\d+)|Thief Gold( Patch)? (?<Version>(?!1\.33|1\.37)\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"Version (?<Version>\d\.\d+) of (Thief 2|Thief2|Thief II)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"(New ?Dark|""New ?Dark"") (is )?required (.? )v?(\.| )?(?<Version>\d\.\d+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(@"(?<Version>(?!1\.33|1\.37)\d\.\d+) Patch",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture)

            // Original regex for reference - slow!
            // @"((?<Name>(""*New *Dark""*( Version| Patch)*|Dark *Engine|(?<!(Love |Being |Penitent |Counter-|Requiem for a |Space ))Thief|(?<!Being )Thief *2|Thief *II|The Metal Age)) *V?(\.| )*(?<Version>\d\.\d+)|\D(?<Version>\d\.\d+) +(version of |(?!\r\n).?)New *Dark(?! *\d\.\d+)|Thief Gold( Patch)* (?<Version>(?!1\.33|1\.37)\d\.\d+))",
        };

        internal static Regex[] AuthorRegexes { get; } =
        {
            new Regex(
                @"(FM|mission|campaign|series) for (Thief|Thief Gold|Thief: The Dark Project|Thief\s*2|Thief 2: The Metal Age)\s+by\s*(?<Author>.+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(
                @"(A )?(Thief|Thief Gold|Thief: The Dark Project|Thief\s*2|Thief 2: The Metal Age) (fan ?(mission|misison|mision)|FM|campaign)\s+by (?<Author>.+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture),
            new Regex(
                @"A(n)? (fan(-| ?)mission|FM)\s+(made\s+)?by\s+(?<Author>.+)",
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture)
        };

        private const string CopyrightSecondPart =
            //language=regexp
            @"(?<Months>( January| February| March| April| May| June| July| August| September| October| November| December))?" +
            //language=regexp
            @"(?(Months)(, ?| ))\d*( by| to)? (?<Author>.+)";

        // Unicode 00A9 = copyright symbol

        internal static Regex AuthorMissionCopyrightRegex { get; } =
            new Regex(
                //language=regexp
                @"^This (level|mission|fan(-| )?mission|FM) is( made)? (\(c\)|\u00A9) ?" + CopyrightSecondPart,
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        // This one is only to be used if we know the above line says "Copyright" or something, because it has
        // an @ as an option for a copyright symbol (used by some Theker missions) and we want to be sure it
        // means what we think it means.
        internal static Regex AuthorGeneralCopyrightIncludeAtSymbolRegex { get; } =
            new Regex(
                //language=regexp
                @"^(Copyright )?(\(c\)|\u00A9|@) ?" + CopyrightSecondPart,
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        internal static Regex AuthorGeneralCopyrightRegex { get; } =
            new Regex(
                //language=regexp
                @"^(Copyright )?(\(c\)|\u00A9) ?" + CopyrightSecondPart,
                RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.ExplicitCapture);

        internal static Regex CopyrightAuthorYearRegex = new Regex(@" \d+.*$", RegexOptions.Compiled);
    }

    /// <summary>
    /// Specialized (therefore fast) sort for titles.str lines only. Anything else is likely to throw an
    /// IndexOutOfRangeException.
    /// </summary>
    internal sealed class TitlesStrNaturalNumericSort : IComparer<string>
    {
        public int Compare(string x, string y)
        {
            if (x == y) return 0;
            if (string.IsNullOrEmpty(x)) return -1;
            if (string.IsNullOrEmpty(y)) return 1;

            int xIndex1;
            var xNum = x.Substring(xIndex1 = x.IndexOf('_') + 1, x.IndexOf(':') - xIndex1);
            int yIndex1;
            var yNum = y.Substring(yIndex1 = y.IndexOf('_') + 1, y.IndexOf(':') - yIndex1);

            while (xNum.Length < 3) xNum = '0' + xNum;
            while (yNum.Length < 3) yNum = '0' + yNum;

            return string.CompareOrdinal(xNum, yNum);
        }
    }
}
