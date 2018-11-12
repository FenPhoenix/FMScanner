using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FMScanner
{
    public sealed class ScanOptions
    {
        /// <summary>
        /// <see langword="true"/> to detect the mission's title.
        /// </summary>
        public bool ScanTitle { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the titles of individual campaign missions.
        /// If the mission is not a campaign, this option has no effect.
        /// </summary>
        public bool ScanCampaignMissionNames { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the mission's author.
        /// </summary>
        public bool ScanAuthor { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the mission's version.
        /// </summary>
        public bool ScanVersion { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the languages the mission supports.
        /// </summary>
        public bool ScanLanguages { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect which game the mission is for (Thief 1 or Thief 2).
        /// </summary>
        public bool ScanGameType { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect whether the mission requires NewDark.
        /// </summary>
        public bool ScanNewDarkRequired { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the minimum NewDark version the mission requires.
        /// If ScanNewDarkRequired is false, this option has no effect.
        /// </summary>
        public bool ScanNewDarkMinimumVersion { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect whether the mission contains custom resources.
        /// </summary>
        public bool ScanCustomResources { get; set; } = true;
    }

    public sealed class ProgressReport
    {
        public string FMName;
        public int FMNumber;
        public int FMsTotal;
        public int Percent;
        public bool Finished;
    }

    public static class Games
    {
        public static string TDP { get; } = "tdp";
        public static string TMA { get; } = "tma";
    }

    public static class FMTypes
    {
        public static string FanMission { get; } = "fanmission";
        public static string Campaign { get; } = "campaign";
    }

    public sealed class Readme
    {
        public string FileName { get; set; } = null;
        public int ArchiveIndex { get; set; } = -1;
    }

    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class ScannedFMData
    {
        public string ArchiveName { get; set; } = null;
        public string Title { get; set; } = null;
        public List<string> AlternateTitles { get; set; } = new List<string>();
        public string Author { get; set; } = null;
        public string Type { get; set; } = null;
        public string[] IncludedMissions { get; set; } = null;
        public string Game { get; set; } = null;
        public string[] Languages { get; set; } = null;
        public string Version { get; set; } = null;
        public bool? NewDarkRequired { get; set; }
        public string NewDarkMinRequiredVersion { get; set; } = null;
        public string OriginalReleaseDate { get; set; } = null;
        public string LastUpdateDate { get; set; } = null;
        public bool? HasCustomScripts { get; set; }
        public bool? HasCustomTextures { get; set; }
        public bool? HasCustomSounds { get; set; }
        public bool? HasCustomObjects { get; set; }
        public bool? HasCustomCreatures { get; set; }
        public bool? HasCustomMotions { get; set; }
        public bool? HasAutomap { get; set; }
        public bool? HasMovies { get; set; }
        public bool? HasMap { get; set; }
        public bool? HasCustomSubtitles { get; set; }
        public string Description { get; set; } = null;
        public List<Readme> Readmes { get; set; } = new List<Readme>();
    }
}
