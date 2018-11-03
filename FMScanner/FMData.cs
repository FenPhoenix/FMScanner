using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace FMScanner
{
    public sealed class ScanOptions
    {
        public bool ScanTitle { internal get; set; } = true;
        /// <summary>
        /// For campaigns, whether to scan for the titles of the individual missions.
        /// </summary>
        public bool ScanCampaignMissionNames { internal get; set; } = true;
        public bool ScanAuthor { internal get; set; } = true;
        public bool ScanVersion { internal get; set; } = true;
        public bool ScanLanguages { internal get; set; } = true;
        /// <summary>
        /// true to scan for game type (Thief 1 or Thief 2) and whether or not NewDark is required.
        /// </summary>
        public bool ScanGameTypeAndNewDark { internal get; set; } = true;
        /// <summary>
        /// true to scan for the minimum required NewDark version, if the mission requires NewDark.
        /// If ScanGameTypeAndNewDark is false, this setting has no effect.
        /// </summary>
        public bool ScanNewDarkMinimumVersion { internal get; set; } = true;
        public bool ScanCustomResources { internal get; set; } = true;
    }

    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    public static class Games
    {
        public static string TDP { get; } = "tdp";
        public static string TMA { get; } = "tma";
    }

    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    public static class FMTypes
    {
        public static string FanMission { get; } = "fanmission";
        public static string Campaign { get; } = "campaign";
    }

    public sealed class ReadmeFile
    {
        public string FileName { get; set; } = null;
        public string[] Lines { get; set; } = null;
        public string Text { get; set; } = null;
        public DateTime LastModifiedDate { get; set; }
    }

    [SuppressMessage("ReSharper", "MemberCanBeInternal")]
    [SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Global")]
    public sealed class ScannedFMData
    {
        public string ArchiveName { get; set; } = null;
        public string Title { get; set; } = null;
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
        public List<ReadmeFile> ReadmeFiles { get; set; } = new List<ReadmeFile>();
    }
}
