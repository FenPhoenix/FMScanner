/*
FMScanner - A fast, thorough, accurate scanner for Thief 1 and Thief 2 fan missions.

Written in 2017-2019 by FenPhoenix.

To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
to this software to the public domain worldwide. This software is distributed without any warranty.

You should have received a copy of the CC0 Public Domain Dedication along with this software.
If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.
*/

using System;
using System.Collections.Generic;

namespace FMScanner
{
    public sealed class ScanOptions
    {
        public static ScanOptions AllFalse => new ScanOptions
        {
            ScanTitle = false,
            ScanCampaignMissionNames = false,
            ScanAuthor = false,
            ScanVersion = false,
            ScanLanguages = false,
            ScanGameType = false,
            ScanNewDarkRequired = false,
            ScanNewDarkMinimumVersion = false,
            ScanCustomResources = false,
            ScanSize = false,
            ScanReleaseDate = false,
            ScanTags = false
        };

        /// <summary>
        /// <see langword="true"/> to detect the mission's title.
        /// </summary>
        public bool ScanTitle { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the titles of individual campaign missions.
        /// If the mission is not a campaign, this option has no effect.
        /// If the mission is for Thief: Deadly Shadows, this option has no effect.
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
        /// If the mission is for Thief: Deadly Shadows, this option has no effect.
        /// </summary>
        public bool ScanLanguages { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect which game the mission is for (Thief 1, Thief 2, or Thief 3).
        /// </summary>
        public bool ScanGameType { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect whether the mission requires NewDark.
        /// If the mission is for Thief: Deadly Shadows, this option has no effect.
        /// </summary>
        public bool ScanNewDarkRequired { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the minimum NewDark version the mission requires.
        /// If ScanNewDarkRequired is false, this option has no effect.
        /// If the mission is for Thief: Deadly Shadows, this option has no effect.
        /// </summary>
        public bool ScanNewDarkMinimumVersion { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect whether the mission contains custom resources.
        /// If the mission is for Thief: Deadly Shadows, this option has no effect.
        /// </summary>
        public bool ScanCustomResources { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the size of the mission. This will differ depending on whether the
        /// mission is a compressed archive or an uncompressed directory.
        /// </summary>
        public bool ScanSize { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the mission's release date.
        /// </summary>
        public bool ScanReleaseDate { get; set; } = true;
        /// <summary>
        /// <see langword="true"/> to detect the mission's tags.
        /// </summary>
        public bool ScanTags { get; set; } = true;
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
        public static string TDS { get; } = "tds";
        public static string Unsupported { get; } = "unsupported";
    }

    public static class FMTypes
    {
        public static string FanMission { get; } = "fanmission";
        public static string Campaign { get; } = "campaign";
    }

    public sealed class ScannedFMData
    {
        public string ArchiveName { get; internal set; } = null;
        public long? Size { get; internal set; } = null;
        public string Title { get; internal set; } = null;
        public List<string> AlternateTitles { get; internal set; } = new List<string>();
        public string Author { get; internal set; } = null;
        public string Type { get; internal set; } = null;
        public string[] IncludedMissions { get; internal set; } = null;
        public string Game { get; internal set; } = null;
        public string[] Languages { get; internal set; } = null;
        public string Version { get; internal set; } = null;
        public bool? NewDarkRequired { get; internal set; }
        public string NewDarkMinRequiredVersion { get; internal set; } = null;
        /// <summary>
        /// Deprecated and will always be blank. Use <see cref="LastUpdateDate"/> instead.
        /// </summary>
        public DateTime? OriginalReleaseDate { get; internal set; } = null;

        private DateTime? _lastUpdateDate;
        public DateTime? LastUpdateDate
        {
            get => _lastUpdateDate;
            internal set
            {
                // Future years will eventually stop being rejected once the current date passes them, but eh
                if (value != null && ((DateTime)value).Year > DateTime.Now.Year)
                {
                    _lastUpdateDate = null;
                }
                else
                {
                    _lastUpdateDate = value;
                }
            }
        }

        public bool? HasCustomScripts { get; internal set; }
        public bool? HasCustomTextures { get; internal set; }
        public bool? HasCustomSounds { get; internal set; }
        public bool? HasCustomObjects { get; internal set; }
        public bool? HasCustomCreatures { get; internal set; }
        public bool? HasCustomMotions { get; internal set; }
        public bool? HasAutomap { get; internal set; }
        public bool? HasMovies { get; internal set; }
        public bool? HasMap { get; internal set; }
        public bool? HasCustomSubtitles { get; internal set; }
        public string Description { get; internal set; } = null;
        public string TagsString { get; internal set; } = null;
    }
}
