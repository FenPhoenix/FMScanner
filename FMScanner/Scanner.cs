using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using MadMilkman.Ini;
using SevenZip;
using static System.IO.Path;
using static System.StringComparison;
using static FMScanner.Methods;
using static FMScanner.FMConstants;
using static FMScanner.Regexes;

namespace FMScanner
{
    #region Classes

    internal sealed class ReadmeInternal
    {
        internal string FileName { get; set; }
        internal int ArchiveIndex { get; set; } = -1;
        internal string[] Lines { get; set; }
        internal string Text { get; set; }
        internal DateTime LastModifiedDate { get; set; }
    }

    internal sealed class NameAndIndex
    {
        internal string Name { get; set; }
        internal int Index { get; set; } = -1;
    }

    #endregion

    [SuppressMessage("ReSharper", "ArrangeStaticMemberQualifier")]
    public class Scanner : IDisposable
    {
        private Stopwatch OverallTimer { get; } = new Stopwatch();

        #region Properties

        private ScanOptions ScanOptions { get; set; } = new ScanOptions();

        private bool FmIsZip { get; set; }

        private string ArchivePath { get; set; }

        private FileStream ArchiveStream { get; set; }
        private ZipArchive Archive { get; set; }

        private string FmWorkingPath { get; set; }

        // Guess I'll leave this one global for reasons
        private List<ReadmeInternal> ReadmeFiles { get; set; } = new List<ReadmeInternal>();

        #endregion

        private enum SpecialLogic
        {
            Title,
            Version,
            NewDarkMinimumVersion,
            Author
        }

        #region Scan one

        public async Task<ScannedFMData>
        Scan(string mission, string tempPath)
        {
            return (await Scan(new List<string> { mission }, tempPath, this.ScanOptions, null,
                CancellationToken.None))[0];
        }

        public async Task<ScannedFMData>
        Scan(string mission, string tempPath, ScanOptions scanOptions)
        {
            return (await Scan(new List<string> { mission }, tempPath, scanOptions, null,
                CancellationToken.None))[0];
        }

        #endregion

        #region Scan many

        public async Task<List<ScannedFMData>>
        Scan(List<string> missions, string tempPath)
        {
            return await Scan(missions, tempPath, this.ScanOptions, null, CancellationToken.None);
        }

        public async Task<List<ScannedFMData>>
        Scan(List<string> missions, string tempPath, ScanOptions scanOptions)
        {
            return await Scan(missions, tempPath, scanOptions, null, CancellationToken.None);
        }

        public async Task<List<ScannedFMData>>
        Scan(List<string> missions, string tempPath, IProgress<ProgressReport> progress,
            CancellationToken cancellationToken)
        {
            return await Scan(missions, tempPath, this.ScanOptions, progress, cancellationToken);
        }

        public async Task<List<ScannedFMData>>
        Scan(List<string> missions, string tempPath, ScanOptions scanOptions, IProgress<ProgressReport> progress,
            CancellationToken cancellationToken)
        {
            #region Checks

            if (string.IsNullOrEmpty(tempPath))
            {
                throw new ArgumentException("Argument is null or empty.", nameof(tempPath));
            }

            if (missions == null) throw new ArgumentNullException(nameof(missions));
            if (missions.Count == 0 || (missions.Count == 1 && string.IsNullOrEmpty(missions[0])))
            {
                throw new ArgumentException("No mission(s) specified.", nameof(missions));
            }

            this.ScanOptions = scanOptions ?? throw new ArgumentNullException(nameof(scanOptions));

            #endregion

            var scannedFMDataList = new List<ScannedFMData>();

            for (var i = 0; i < missions.Count; i++)
            {
                #region Init

                var fm = missions[i];
                FmIsZip = fm.ExtEqualsI(".zip") || fm.ExtEqualsI(".7z");

                ArchiveStream?.Dispose();
                Archive?.Dispose();

                if (FmIsZip)
                {
                    ArchivePath = fm;
                    FmWorkingPath = Path.Combine(tempPath, GetFileNameWithoutExtension(ArchivePath).Trim());
                }
                else
                {
                    FmWorkingPath = fm;
                }

                ReadmeFiles = new List<ReadmeInternal>();

                #endregion

                await Task.Run(() => scannedFMDataList.Add(ScanCurrentFM()), cancellationToken);

                #region Report progress and handle cancellation

                cancellationToken.ThrowIfCancellationRequested();

                if (progress == null) continue;

                var progressReport = new ProgressReport
                {
                    FMName = missions[i],
                    FMNumber = i + 1,
                    FMsTotal = missions.Count,
                    Percent = (100 * (i + 1)) / missions.Count,
                    Finished = i == missions.Count - 1
                };
                progress.Report(progressReport);

                #endregion
            }

            return scannedFMDataList;
        }

        #endregion

        private ScannedFMData ScanCurrentFM()
        {
            OverallTimer.Restart();

            #region Check for and setup 7-Zip

            bool fmIsSevenZip = false;
            if (FmIsZip && ArchivePath.ExtEqualsI(".7z"))
            {
                FmIsZip = false;
                fmIsSevenZip = true;

                try
                {
                    using (var sze = new SevenZipExtractor(ArchivePath) { PreserveDirectoryStructure = true })
                    {
                        sze.ExtractArchive(FmWorkingPath);
                    }
                }
                catch (Exception)
                {
                    // Third party thing, doesn't tell you what exceptions it can throw, whatever
                    DeleteFmWorkingPath(FmWorkingPath);
                    return null;
                }
            }

            #endregion

            #region Check for and setup Zip

            if (FmIsZip)
            {
                Debug.WriteLine(@"----------" + ArchivePath);

                if (ArchivePath.ExtEqualsI(".zip"))
                {
                    ArchiveStream = new FileStream(ArchivePath, FileMode.Open, FileAccess.Read);
                    try
                    {
                        Archive = new ZipArchive(ArchiveStream, ZipArchiveMode.Read);
                    }
                    catch (InvalidDataException)
                    {
                        // Invalid zip file, whatever, move on
                        return null;
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                if (!Directory.Exists(FmWorkingPath))
                {
                    throw new DirectoryNotFoundException("Directory not found: " + FmWorkingPath);
                }
                Debug.WriteLine(@"----------" + FmWorkingPath);
            }

            #endregion

            var baseDirFiles = new List<NameAndIndex>();
            var misFiles = new List<NameAndIndex>();
            var usedMisFiles = new List<NameAndIndex>();
            var stringsDirFiles = new List<NameAndIndex>();
            var intrfaceDirFiles = new List<NameAndIndex>();
            string[] titlesStrFileLines;

            #region Cache FM data

            (baseDirFiles, misFiles, usedMisFiles, stringsDirFiles, intrfaceDirFiles, titlesStrFileLines)
                = ReadAndCacheFMData();

            if (!baseDirFiles.Any() || !misFiles.Any() || !usedMisFiles.Any())
            {
                if (fmIsSevenZip) DeleteFmWorkingPath(FmWorkingPath);
                return null;
            }

            #endregion

            var fmData = new ScannedFMData();

            if (ScanOptions.ScanCustomResources)
            {
                // Pass in a reference and let it assign directly... trade-off purity for speed and convenience
                CheckForCustomResources(fmData, baseDirFiles);
            }

            fmData.Type = usedMisFiles.Count > 1 ? FMTypes.Campaign : FMTypes.FanMission;

            void SetOrAddTitle(string value)
            {
                if (string.IsNullOrEmpty(value)) return;

                value = CleanupTitle(value);

                if (string.IsNullOrEmpty(fmData.Title))
                {
                    fmData.Title = value;
                }
                else if (!fmData.Title.EqualsI(value) && !fmData.AlternateTitles.ContainsI(value))
                {
                    fmData.AlternateTitles.Add(value);
                }
            }

            #region Check info files

            if (ScanOptions.ScanTitle || ScanOptions.ScanAuthor || ScanOptions.ScanVersion)
            {
                var fmInfoXml = baseDirFiles.FirstOrDefault(x => x.Name.ContainsI(FMFiles.FMInfoXml));
                if (fmInfoXml != null)
                {
                    var t = ReadFmInfoXml(fmInfoXml);
                    if (ScanOptions.ScanTitle) SetOrAddTitle(t.Item1);
                    if (ScanOptions.ScanAuthor) fmData.Author = t.Item2;
                    fmData.Version = t.Item3;
                }
            }
            {
                var fmIni = baseDirFiles.FirstOrDefault(x => x.Name.ContainsI(FMFiles.FMIni));
                if (fmIni != null)
                {
                    var t = ReadFmIni(fmIni);
                    if (ScanOptions.ScanTitle) SetOrAddTitle(t.Item1);
                    if (ScanOptions.ScanAuthor) fmData.Author = t.Item2;
                    fmData.Description = t.Item3;
                    fmData.LastUpdateDate = t.Item4;
                }
            }

            #endregion

            ReadAndCacheReadmeFiles(baseDirFiles);

            #region Title and IncludedMissions

            if (ScanOptions.ScanTitle)
            {
                SetOrAddTitle(GetTitleFromTitlesStrZeroLine(titlesStrFileLines));
            }

            if (ScanOptions.ScanTitle || ScanOptions.ScanCampaignMissionNames)
            {
                var t = GetMissionNames(titlesStrFileLines, misFiles, usedMisFiles);
                if (ScanOptions.ScanTitle) SetOrAddTitle(t.Item1);
                if (ScanOptions.ScanCampaignMissionNames) fmData.IncludedMissions = t.Item2;
            }

            if (ScanOptions.ScanTitle)
            {
                SetOrAddTitle(
                    GetValueFromReadme(SpecialLogic.Title, "Title", "Mission Title", "Mission title",
                        "Mission Name", "Mission name", "Level Name", "Level name", "Mission:", "Mission ",
                        "Campaign Title", "Campaign title", "The name of Mission:"));

                SetOrAddTitle(GetTitleFromNewGameStrFile(intrfaceDirFiles));
            }

            #endregion

            #region Author

            if (ScanOptions.ScanAuthor)
            {
                if (fmData.Author.IsEmpty())
                {
                    // TODO: Do I want to check AlternateTitles for StartsWithI("By ") as well?
                    fmData.Author =
                        GetValueFromReadme(SpecialLogic.Author, fmData.Title.StartsWithI("By "),
                            "Author", "Authors", "Autor",
                            "Created by", "Devised by", "Designed by", "Author=", "Made by",
                            "FM Author", "Mission Author", "Mission author", "The author:",
                            "author:");
                }

                if (!fmData.Author.IsEmpty())
                {
                    // Remove email addresses from the end of author names
                    var match = AuthorEmailRegex.Match(fmData.Author);
                    if (match.Success)
                    {
                        fmData.Author = fmData.Author.Remove(match.Index, match.Length).Trim();
                    }
                }
            }

            #endregion

            if (ScanOptions.ScanVersion && fmData.Version.IsEmpty())
            {
                fmData.Version = GetVersion();
            }

            if (ScanOptions.ScanLanguages)
            {
                fmData.Languages = GetLanguages(baseDirFiles);
            }

            #region NewDark/GameType checks

            if (ScanOptions.ScanNewDarkRequired || ScanOptions.ScanGameType)
            {
                var t = GetGameTypeAndEngine(usedMisFiles);
                if (ScanOptions.ScanNewDarkRequired) fmData.NewDarkRequired = t.Item1;
                if (ScanOptions.ScanGameType) fmData.Game = t.Item2;
            }

            if (fmData.NewDarkRequired == true && ScanOptions.ScanNewDarkMinimumVersion)
            {
                fmData.NewDarkMinRequiredVersion = GetValueFromReadme(SpecialLogic.NewDarkMinimumVersion);
            }

            #endregion

            if (fmIsSevenZip) DeleteFmWorkingPath(FmWorkingPath);

            OverallTimer.Stop();

            Debug.WriteLine(@"This FM took:\r\n" + OverallTimer.Elapsed.ToString(@"hh\:mm\:ss\.fffffff"));

            fmData.ArchiveName = FmIsZip ? GetFileName(ArchivePath) : GetFileName(FmWorkingPath);

            foreach (var r in ReadmeFiles)
            {
                fmData.Readmes.Add(new Readme { FileName = r.FileName, ArchiveIndex = r.ArchiveIndex });
            }

            return fmData;
        }

        private (List<NameAndIndex> BaseDirFiles, List<NameAndIndex> MisFiles,
        List<NameAndIndex> UsedMisFiles, List<NameAndIndex> StringsDirFiles,
        List<NameAndIndex> intrfaceDirFiles, string[] TitlesStrFileLines)
        ReadAndCacheFMData()
        {
            string[] titlesStrFileLines = { };
            var misFiles = new List<NameAndIndex>();
            var usedMisFiles = new List<NameAndIndex>();
            var baseDirFiles = new List<NameAndIndex>();
            var stringsDirFiles = new List<NameAndIndex>();
            var intrfaceDirFiles = new List<NameAndIndex>();

            var nullRet = ((List<NameAndIndex>)null, (List<NameAndIndex>)null, (List<NameAndIndex>)null,
                (List<NameAndIndex>)null, (List<NameAndIndex>)null, (string[])null);

            #region Add BaseDirFiles

            try
            {
                if (FmIsZip)
                {
                    for (var i = 0; i < Archive.Entries.Count; i++)
                    {
                        var e = Archive.Entries[i];
                        if (!e.FullName.Contains('/') && e.FullName.Contains('.'))
                        {
                            baseDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                        }
                        else if (e.FullName.StartsWithI(FMDirs.Strings + '/'))
                        {
                            stringsDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                        }
                        else if (e.FullName.StartsWithI(FMDirs.Intrface + '/'))
                        {
                            intrfaceDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                        }
                    }
                }
                else
                {
                    foreach (var f in EnumFiles("*", SearchOption.TopDirectoryOnly))
                    {
                        baseDirFiles.Add(new NameAndIndex { Name = GetFileName(f) });
                    }

                    foreach (var f in EnumFiles(FMDirs.Strings, "*", SearchOption.AllDirectories))
                    {
                        stringsDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length + 1) });
                    }

                    foreach (var f in EnumFiles(FMDirs.Intrface, "*", SearchOption.AllDirectories))
                    {
                        intrfaceDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length + 1) });
                    }

                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return nullRet;
            }

            #endregion

            #region Add MisFiles and check for none

            for (var i = 0; i < baseDirFiles.Count; i++)
            {
                var f = baseDirFiles[i];
                if (f.Name.ExtEqualsI(".mis"))
                {
                    misFiles.Add(new NameAndIndex { Name = GetFileName(f.Name), Index = f.Index });
                }
            }

            if (!misFiles.Any()) return nullRet;

            #endregion

            #region Cache list of used .mis files

            NameAndIndex missFlag = null;

            char ds = FmIsZip ? '/' : Path.DirectorySeparatorChar;

            if (stringsDirFiles.Any())
            {
                // I don't remember if I need to search in this exact order, so uh... not rockin' the boat.
                missFlag =
                    stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Strings + ds + FMFiles.MissFlag))
                    ?? stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Strings + ds + "english" + ds + FMFiles.MissFlag))
                    ?? stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EndsWithI(ds + FMFiles.MissFlag));
            }

            if (missFlag != null)
            {
                string[] mfLines;

                if (FmIsZip)
                {
                    var e = Archive.Entries[missFlag.Index];
                    using (var es = e.Open())
                    {
                        mfLines = ReadAllLinesE(es, e.Length);
                    }
                }
                else
                {
                    mfLines = ReadAllLinesE(Path.Combine(FmWorkingPath, missFlag.Name));
                }

                for (var i = 0; i < misFiles.Count; i++)
                {
                    var mf = misFiles[i];
                    var mfNoExt = GetFileNameWithoutExtension(mf.Name);
                    if (mfNoExt.StartsWithI("miss") && mfNoExt.Length > 4)
                    {
                        for (var j = 0; j < mfLines.Length; j++)
                        {
                            var line = mfLines[j];
                            if (line.StartsWithI("miss_" + mfNoExt.Substring(4) + ":") &&
                                line.IndexOf('\"') > -1 &&
                                !line.Substring(line.IndexOf('\"')).StartsWithI("\"skip\""))
                            {
                                usedMisFiles.Add(mf);
                            }
                        }
                    }
                }
            }

            // Fallback we hope never happens, but... sometimes it does
            if (!usedMisFiles.Any()) usedMisFiles = misFiles;

            #endregion

            #region Cache titles.str

            // Do not change search order: strings/english, strings, strings/[any other language]
            var titlesStrDirs = new List<string>();

            titlesStrDirs.AddRange(
                from f in FMFiles.TitlesFiles
                select FMDirs.Strings + "/english/" + f);

            titlesStrDirs.AddRange(
                from f in FMFiles.TitlesFiles
                select FMDirs.Strings + '/' + f);

            foreach (var lang in Languages.Where(x => !x.EqualsI("english")))
            {
                titlesStrDirs.AddRange(
                    from f in FMFiles.TitlesFiles
                    select FMDirs.Strings + '/' + lang + '/' + f);
            }

            foreach (var titlesFileLocation in titlesStrDirs)
            {
                var titlesFile =
                    FmIsZip
                        ? stringsDirFiles.FirstOrDefault(x => x.Name.EqualsI(titlesFileLocation))
                        : new NameAndIndex { Name = Path.Combine(FmWorkingPath, titlesFileLocation) };

                if (titlesFile == null || !FmIsZip && !File.Exists(titlesFile.Name)) continue;

                if (FmIsZip)
                {
                    var e = Archive.Entries[titlesFile.Index];
                    if (e != null)
                    {
                        using (var es = e.Open())
                        {
                            titlesStrFileLines = ReadAllLinesE(es, e.Length);
                        }
                    }
                }
                else
                {
                    titlesStrFileLines = ReadAllLinesE(titlesFile.Name);
                }

                // The corner cases...
                for (int i = 0; i < titlesStrFileLines.Length; i++)
                {
                    titlesStrFileLines[i] = titlesStrFileLines[i].TrimStart();
                }

                break;
            }

            #endregion

            return (baseDirFiles, misFiles, usedMisFiles, stringsDirFiles, intrfaceDirFiles, titlesStrFileLines);
        }

        // Willing to hand this one the entire object because you can tell with a simple glance which properties
        // it's setting, and having it return a set of values gets sort of absurd no matter how you look at it.
        private void CheckForCustomResources(ScannedFMData scannedFMData, List<NameAndIndex> baseDirFiles)
        {
            var baseDirFolders = (
                FmIsZip
                    ? (from e in Archive.Entries
                       let f = e.FullName.TrimEnd('/')
                       where f.Contains('/') || !f.Contains('.')
                       select f.Contains('/') ? f.Substring(0, f.IndexOf('/')) : f)
                    .Distinct().AsParallel()
                    : from f in Directory.EnumerateDirectories(FmWorkingPath, "*",
                        SearchOption.TopDirectoryOnly)
                      select DirName(f)).ToArray();

            scannedFMData.HasMap =
                FmIsZip
                    ? Archive.Entries.Any(f => MapRegex.Match(f.FullName).Success)
                    : baseDirFolders.ContainsI(FMDirs.Intrface) &&
                      FastIO.FilesExistSearchAllSkipTop(Combine(FmWorkingPath, FMDirs.Intrface), "page0*.*");

            scannedFMData.HasAutomap =
                FmIsZip
                    ? Archive.Entries.Any(f => AutomapRegex.Match(f.FullName).Success)
                    : baseDirFolders.ContainsI(FMDirs.Intrface) &&
                      FastIO.FilesExistSearchAllSkipTop(Combine(FmWorkingPath, FMDirs.Intrface), "*ra.bin");

            // Definitely a clever deduction, definitely not a sneaky hack for GatB-T2
            if (scannedFMData.HasAutomap == true) scannedFMData.HasMap = true;

            scannedFMData.HasCustomMotions =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Motions + '/') &&
                        MotionFileExtensions.Any(f.FullName.EndsWithI))
                    : baseDirFolders.ContainsI(FMDirs.Motions) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Motions),
                          MotionFilePatterns);

            scannedFMData.HasMovies =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Movies + '/') &&
                        EndsWithExtensionRegex.Match(f.FullName).Success)
                    : baseDirFolders.ContainsI(FMDirs.Movies) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Movies), "*");

            scannedFMData.HasCustomTextures =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Fam + '/') &&
                        ImageFileExtensions.Any(f.FullName.EndsWithI))
                    : baseDirFolders.ContainsI(FMDirs.Fam) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Fam),
                          ImageFilePatterns);

            scannedFMData.HasCustomObjects =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Obj + '/') &&
                        f.FullName.EndsWithI(".bin"))
                    : baseDirFolders.ContainsI(FMDirs.Obj) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Obj), "*.bin");

            scannedFMData.HasCustomCreatures =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Mesh + '/') &&
                        f.FullName.EndsWithI(".bin"))
                    : baseDirFolders.ContainsI(FMDirs.Mesh) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Mesh), "*.bin");

            scannedFMData.HasCustomScripts =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        (!f.FullName.Contains('/') &&
                         ScriptFileExtensions.Any(f.FullName.EndsWithI)) ||
                        (f.FullName.StartsWithI(FMDirs.Scripts + '/') &&
                         EndsWithExtensionRegex.Match(f.FullName).Success))
                    : baseDirFiles.Any(x => ScriptFileExtensions.ContainsI(GetExtension(x.Name))) ||
                      (baseDirFolders.ContainsI(FMDirs.Scripts) &&
                       FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Scripts), "*"));

            scannedFMData.HasCustomSounds =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Snd + '/') &&
                        EndsWithExtensionRegex.Match(f.FullName).Success)
                    : baseDirFolders.ContainsI(FMDirs.Snd) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Snd), "*");

            scannedFMData.HasCustomSubtitles =
                FmIsZip
                    ? Archive.Entries.Any(f =>
                        f.FullName.StartsWithI(FMDirs.Subtitles + '/') &&
                        f.FullName.EndsWithI(".sub"))
                    : baseDirFolders.ContainsI(FMDirs.Subtitles) &&
                      FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Subtitles), "*.sub");
        }

        private Tuple<string, string, string>
        ReadFmInfoXml(NameAndIndex file)
        {
            string title = null;
            string author = null;
            string version = null;

            var fmInfoXml = new XmlDocument();

            #region Load XML

            if (FmIsZip)
            {
                var e = Archive.Entries[file.Index];
                using (var es = e.Open())
                {
                    fmInfoXml.Load(es);
                }
            }
            else
            {
                fmInfoXml.Load(Path.Combine(FmWorkingPath, file.Name));
            }

            #endregion

            if (ScanOptions.ScanTitle)
            {
                var xTitle = fmInfoXml.GetElementsByTagName("title");
                if (xTitle.Count > 0) title = xTitle[0].InnerText;
            }

            if (ScanOptions.ScanAuthor)
            {
                var xAuthor = fmInfoXml.GetElementsByTagName("author");
                if (xAuthor.Count > 0) author = xAuthor[0].InnerText;
            }

            if (ScanOptions.ScanVersion)
            {
                var xVersion = fmInfoXml.GetElementsByTagName("version");
                if (xVersion.Count > 0) version = xVersion[0].InnerText;
            }

            // These files also specify languages and whether the mission has custom stuff, but we're not going
            // to trust what we're told - we're going to detect that stuff by looking at what's actually there.

            return Tuple.Create(title, author, version);
        }

        private Tuple<string, string, string, string>
        ReadFmIni(NameAndIndex file)
        {
            var nullRet = Tuple.Create((string)null, (string)null, (string)null, (string)null);

            string title = null;
            string author = null;
            string description = null;
            string lastUpdateDate = null;

            var ini = new IniFile();

            FMIniData fmIni;

            using (var fmIniStream = new MemoryStream())
            {
                #region Load INI

                var fmIniFileOnDisk = "";

                long fmIniLength = 0;

                if (FmIsZip)
                {
                    var e = Archive.Entries[file.Index];
                    fmIniLength = e.Length;
                    using (var es = e.Open())
                    {
                        es.CopyTo(fmIniStream);
                        fmIniStream.Position = 0;
                    }
                }
                else
                {
                    fmIniFileOnDisk = Path.Combine(FmWorkingPath, file.Name);
                }

                var iniText = FmIsZip
                    ? ReadAllTextE(fmIniStream, fmIniLength, streamIsSeekable: true)
                    : ReadAllTextE(fmIniFileOnDisk);

                if (string.IsNullOrEmpty(iniText)) return nullRet;

                using (var sr = new StringReader(iniText))
                {
                    ini.Load(sr);
                }

                fmIni = ini.Sections.First().Deserialize<FMIniData>();

                #endregion

                #region Description

                // Description is supposed to be one line with \n for line breaks, but people just aren't
                // consistent with the format :[
                if (!string.IsNullOrEmpty(fmIni.Descr) && fmIni.Descr[0] == '\"')
                {
                    // Read the whole file again. To avoid this I'd have to write my own parser. If I find more
                    // than just the one file with this multiline-quoted format, maybe I will.
                    if (FmIsZip) fmIniStream.Position = 0;
                    var iniAllText =
                        FmIsZip
                            ? ReadAllTextE(fmIniStream, fmIniLength)
                            : ReadAllTextE(fmIniFileOnDisk);

                    // TODO: Theeeeeeoretically incorrect. Doesn't look at start of line (or start of file).
                    var descr = iniAllText.Substring(iniAllText.IndexOf("Descr=", Ordinal) + 6);

                    // Remove starting quote char
                    descr = descr.Substring(1);

                    // Match an actual quote char (not \")
                    var match = Regex.Match(descr, @"[^\\](?<EndQuote>"")");
                    if (match.Success)
                    {
                        fmIni.Descr = descr.Substring(0, match.Groups["EndQuote"].Index);
                    }
                }
                else if (!fmIni.Descr.IsEmpty())
                {
                    fmIni.Descr = fmIni.Descr
                        .Replace(@"\t", "\t")
                        .Replace(@"\r\n", "\r\n")
                        .Replace(@"\r", "\r\n")
                        .Replace(@"\n", "\r\n")
                        .Replace(@"\""", "\"");
                }

                #endregion
            }

            #region Tags

            // TODO: Get other info from tags: genre, length, whatever
            if (ScanOptions.ScanAuthor)
            {
                var tags = ini.Sections.First().Keys["Tags"];
                if (tags != null)
                {
                    var tagsList = tags.Value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);

                    var authors = tagsList.Where(x => x.StartsWithI("author:"));

                    var authorString = "";
                    var first = true;
                    foreach (var a in authors)
                    {
                        if (!first && !authorString.EndsWith(", ")) authorString += ", ";
                        authorString += a.Substring(a.IndexOf(':') + 1).Trim();

                        first = false;
                    }
                    author = authorString;
                }
            }

            #endregion

            if (ScanOptions.ScanTitle) title = fmIni.NiceName;
            lastUpdateDate = fmIni.ReleaseDate;
            description = fmIni.Descr;

            /*
               Notes:
                - fm.ini can specify a readme file, but it may not be the one we're looking for, as far as
                  detecting values goes. Reading all .txt and .rtf files is slightly slower but more accurate.

                - Although fm.ini wasn't used before NewDark, its presence doesn't necessarily mean the mission
                  is NewDark-only. Sturmdrang Peak has it but doesn't require NewDark, for instance.
            */
            return Tuple.Create(title, author, description, lastUpdateDate);
        }

        // Because RTF files can have embedded images, their size can far exceed that normally expected of a
        // readme. To save time and memory, this method strips out such large data blocks before passing the
        // result to a WinForms RichTextBox for final conversion to plain text.
        private static bool GetRtfFileLinesAndText(Stream stream, int streamLength, RichTextBox rtfBox)
        {
            if (stream.Position > 0) stream.Position = 0;

            // Don't parse files small enough to be unlikely to have embedded images; otherwise we're just
            // parsing it twice for nothing
            if (streamLength < 262_144)
            {
                rtfBox.LoadFile(stream, RichTextBoxStreamType.RichText);
                stream.Position = 0;
                return true;
            }

            var byteList = new List<byte>();
            byte stack = 0;
            for (long i = 0; i < streamLength; i++)
            {
                // Just in case there's a malformed file or something
                if (stack > 100)
                {
                    stream.Position = 0;
                    return false;
                }

                var b = stream.ReadByte();
                if (b == '{')
                {
                    if (i < streamLength - 11)
                    {
                        stream.Read(RtfTags.Bytes11, 0, RtfTags.Bytes11.Length);

                        Array.Copy(RtfTags.Bytes11, RtfTags.Bytes10, 10);
                        Array.Copy(RtfTags.Bytes11, RtfTags.Bytes5, 5);

                        if (RtfTags.Bytes10.SequenceEqual(RtfTags.shppictB) ||
                            RtfTags.Bytes10.SequenceEqual(RtfTags.objDatatB) ||
                            RtfTags.Bytes11.SequenceEqual(RtfTags.nonshppictB) ||
                            RtfTags.Bytes5.SequenceEqual(RtfTags.pictB))
                        {
                            stack++;
                            stream.Position -= RtfTags.Bytes11.Length;
                            continue;
                        }

                        if (stack > 0) stack++;
                        stream.Position -= RtfTags.Bytes11.Length;
                    }
                }
                else if (b == '}' && stack > 0)
                {
                    stack--;
                    continue;
                }

                if (stack == 0) byteList.Add((byte)b);
            }

            using (var trimmedMemStream = new MemoryStream(byteList.ToArray()))
            {
                rtfBox.LoadFile(trimmedMemStream, RichTextBoxStreamType.RichText);
                stream.Position = 0;
                return true;
            }
        }

        private void ReadAndCacheReadmeFiles(List<NameAndIndex> baseDirFiles)
        {
            // Note: .wri files look like they may be just plain text with garbage at the top. Shrug.
            // Treat 'em like plaintext and see how it goes.

            var readmes =
                (from fd in baseDirFiles
                 where new[] { ".txt", ".rtf", ".wri" }.Any(x => fd.Name.ExtEqualsI(x)) ||
                    fd.Name.ExtIsHtml()
                 select fd).ToList();

            // Maybe could combine these checks, but this works for now
            foreach (var readmeFile in readmes)
            {
                ZipArchiveEntry readmeEntry = null;

                if (FmIsZip) readmeEntry = Archive.Entries[readmeFile.Index];

                int fileLen = FmIsZip
                    ? (int)readmeEntry.Length
                    : (int)new FileInfo(Path.Combine(FmWorkingPath, readmeFile.Name)).Length;

                // try-finally instead of using, because we only want to initialize the readme stream if FmIsZip
                Stream readmeStream = null;
                try
                {
                    var readmeFileOnDisk = "";

                    string fileName;
                    DateTime lastModifiedDate;

                    long readmeLength = 0;

                    if (FmIsZip)
                    {
                        fileName = readmeEntry.Name;
                        lastModifiedDate = readmeEntry.LastWriteTime.DateTime;
                    }
                    else
                    {
                        readmeFileOnDisk = Path.Combine(FmWorkingPath, readmeFile.Name);
                        var fi = new FileInfo(readmeFileOnDisk);
                        fileName = fi.Name;
                        lastModifiedDate = fi.LastWriteTime;
                    }

                    ReadmeFiles.Add(new ReadmeInternal
                    {
                        FileName = fileName,
                        ArchiveIndex = readmeFile.Index,
                        LastModifiedDate = lastModifiedDate
                    });

                    if (readmeFile.Name.ExtIsHtml() || !readmeFile.Name.IsEnglishReadme()) continue;

                    if (FmIsZip)
                    {
                        readmeLength = readmeEntry.Length;
                        readmeStream = new MemoryStream(fileLen);

                        using (var es = readmeEntry.Open())
                        {
                            es.CopyTo(readmeStream);
                            readmeStream.Position = 0;
                        }
                    }

                    var rtfHeader = new char[6];
                    using (var sr = FmIsZip
                        ? new StreamReader(readmeStream, Encoding.ASCII, false, 6, true)
                        : new StreamReader(readmeFileOnDisk, Encoding.ASCII, false, 6))
                    {
                        sr.ReadBlock(rtfHeader, 0, 6);
                    }

                    if (FmIsZip) readmeStream.Position = 0;

                    // Saw one ".rtf" that was actually a plaintext file, and one vice versa. So detect by
                    // header alone.
                    if (string.Concat(rtfHeader).EqualsI(@"{\rtf1"))
                    {
                        using (var rtfBox = new RichTextBox())
                        {
                            bool success;
                            if (FmIsZip)
                            {
                                success = GetRtfFileLinesAndText(readmeStream, fileLen, rtfBox);
                            }
                            else
                            {
                                using (var fs = new FileStream(readmeFileOnDisk, FileMode.Open, FileAccess.Read))
                                {
                                    success = GetRtfFileLinesAndText(fs, fileLen, rtfBox);
                                }
                            }

                            if (success)
                            {
                                ReadmeFiles.Last().Lines = rtfBox.Lines;
                                ReadmeFiles.Last().Text = rtfBox.Text;
                            }
                        }
                    }
                    else
                    {
                        ReadmeFiles.Last().Lines = FmIsZip
                            ? ReadAllLinesE(readmeStream, readmeLength)
                            : ReadAllLinesE(readmeFileOnDisk);
                        ReadmeFiles.Last().Text = string.Join("\r\n", ReadmeFiles.Last().Lines);
                    }
                }
                finally
                {
                    readmeStream?.Dispose();
                }
            }
        }

        private static string GetTitleFromTitlesStrZeroLine(string[] titlesFileLines)
        {
            if (titlesFileLines == null || !titlesFileLines.Any()) return null;

            for (int i = 0; i < titlesFileLines.Length; i++)
            {
                var line = titlesFileLines[i];
                if (line.StartsWithI("title_0:") && line.Count(x => x == '\"') > 1)
                {
                    line = line.Substring(line.IndexOf('\"') + 1);
                    line = line.Substring(0, line.IndexOf('\"'));
                    return line;
                }
            }

            return null;
        }

        private Tuple<string, string[]>
        GetMissionNames(string[] titlesStrFileLines, List<NameAndIndex> misFiles,
            List<NameAndIndex> usedMisFiles)
        {
            string retTitle = null;
            string[] retIncludedMissions = null;

            var nullRet = Tuple.Create((string)null, (string[])null);

            if (titlesStrFileLines == null || titlesStrFileLines.Length == 0)
            {
                return nullRet;
            }

            var titles = new List<string>();

            // There's a way to do this with an IEqualityComparer, but no, for reasons
            string[] tfLinesD;
            {
                var temp = new List<string>();
                foreach (var line in titlesStrFileLines.Where(x => x.Contains(':') && x.StartsWithI("title_")))
                {
                    if (!temp.Any(x => x.Contains(':') && x.StartsWithI(line.Substring(0, line.IndexOf(':')))))
                    {
                        temp.Add(line);
                    }
                }

                tfLinesD = temp.ToArray();
            }

            Array.Sort(tfLinesD, new TitlesStrNaturalNumericSort());

            string titleNum = null;
            string title = null;
            for (int line = 0; line < tfLinesD.Length; line++)
            {
                for (int umf = 0; umf < usedMisFiles.Count; umf++)
                {
                    titleNum = tfLinesD[line].Substring("title_".Length);
                    titleNum = titleNum.Substring(0, titleNum.IndexOf(':')).Trim();

                    var startOfQuotedSection =
                        tfLinesD[line].Substring(tfLinesD[line].IndexOf(':') + 1).Trim();

                    var titleStringMatch = TitleStringQuotedRegex.Match(startOfQuotedSection);
                    if (!titleStringMatch.Success) continue;

                    title = titleStringMatch.Groups["Title"].Value;
                    var umfNoExt = usedMisFiles[umf].Name.RemoveExtension();
                    if (umfNoExt != null && umfNoExt.StartsWithI("miss") && umfNoExt.Length > 4 &&
                        ScanOptions.ScanCampaignMissionNames &&
                        titleNum == umfNoExt.Substring(4))
                    {
                        titles.Add(CleanupTitle(title));
                    }
                }

                if (ScanOptions.ScanTitle &&
                    retTitle.IsEmpty() &&
                    line == tfLinesD.Length - 1 &&
                    !string.IsNullOrEmpty(titleNum) &&
                    !string.IsNullOrEmpty(title) &&
                    !usedMisFiles.Any(x => x.Name.ContainsI("miss" + titleNum + ".mis")) &&
                    misFiles.Any(x => x.Name.ContainsI("miss" + titleNum + ".mis")))
                {
                    retTitle = CleanupTitle(title);
                    if (!ScanOptions.ScanCampaignMissionNames) break;
                }
            }

            if (titles.Count > 0)
            {
                if (ScanOptions.ScanTitle && titles.Count == 1)
                {
                    retTitle = titles.First();
                }
                else if (ScanOptions.ScanCampaignMissionNames)
                {
                    retIncludedMissions = titles.ToArray();
                }
            }

            return Tuple.Create(retTitle, retIncludedMissions);
        }

        private string GetValueFromReadme(SpecialLogic specialLogic, params string[] keys)
        {
            return GetValueFromReadme(specialLogic, false, keys);
        }

        private string
        GetValueFromReadme(SpecialLogic specialLogic, bool fmTitleStartsWithBy, params string[] keys)
        {
            string ret = null;

            foreach (var file in ReadmeFiles.Where(x => !x.FileName.ExtIsHtml() && x.FileName.IsEnglishReadme()))
            {
                if (specialLogic == SpecialLogic.NewDarkMinimumVersion)
                {
                    var ndv = GetNewDarkVersionFromText(file.Text);
                    if (!string.IsNullOrEmpty(ndv)) return ndv;
                }
                else
                {
                    if (specialLogic == SpecialLogic.Author)
                    {
                        /*
                            Check this first so as to avoid:
                        
                            Briefing Movie
                            Created by Yandros using VideoPad by NCH Software
                        */
                        ret = GetAuthorFromTopOfReadme(file.Lines, fmTitleStartsWithBy);
                        if (!string.IsNullOrEmpty(ret)) return ret;
                    }

                    ret = GetValueFromLines(specialLogic, keys, file.Lines);
                    if (string.IsNullOrEmpty(ret))
                    {
                        if (specialLogic == SpecialLogic.Author)
                        {
                            ret = GetAuthorFromText(file.Text);
                            if (!string.IsNullOrEmpty(ret)) return ret;
                        }
                    }
                    else
                    {
                        return ret;
                    }
                }
            }

            if (specialLogic == SpecialLogic.Author && string.IsNullOrEmpty(ret))
            {
                // We do this separately for performance and clarity; it's an uncommon case involving regex
                // searching and we don't want to run it unless we have to. Also, it's specific enough that we
                // don't really want to shoehorn it into the standard line search.
                ret = GetAuthorFromCopyrightMessage();
            }

            return ret;
        }

        private static string GetValueFromLines(SpecialLogic specialLogic, string[] keys, string[] lines)
        {
            string ret = null;

            foreach (var line in lines)
            {
                var lineStartTrimmed = line.TrimStart();

                // Either in given case or in all caps, but not in lowercase, because that's given me at least
                // one false positive
                if (!keys.Any(x =>
                    lineStartTrimmed.StartsWith(x) || lineStartTrimmed.StartsWith(x.ToUpperInvariant())))
                {
                    continue;
                }

                if (specialLogic == SpecialLogic.Version &&
                    (lineStartTrimmed.StartsWithI("Version History") ||
                     lineStartTrimmed.ContainsI("NewDark") ||
                     lineStartTrimmed.ContainsI("64 Cubed") ||
                     Regex.Match(lineStartTrimmed, @"\d\.\d+\+").Success))
                {
                    continue;
                }
                else if (specialLogic == SpecialLogic.Author && lineStartTrimmed.StartsWithI("Authors note"))
                {
                    continue;
                }

                if (keys.Any(x =>
                    Regex.Match(lineStartTrimmed, @"^" + x + @"\s*(?<Separator>(:|-))", RegexOptions.IgnoreCase)
                        .Success))
                {
                    int indexColon = lineStartTrimmed.IndexOf(':');
                    int indexDash = lineStartTrimmed.IndexOf('-');

                    int index = indexColon > -1 && indexDash > -1
                        ? Math.Min(indexColon, indexDash)
                        : Math.Max(indexColon, indexDash);

                    ret = lineStartTrimmed.Substring(index + 1).Trim();
                    if (!string.IsNullOrEmpty(ret)) break;
                }
                else
                {
                    // Don't detect "Version "; too many false positives
                    if (specialLogic == SpecialLogic.Version) break;

                    if (specialLogic == SpecialLogic.Title &&
                        lineStartTrimmed.StartsWithI("Title & Description"))
                    {
                        break;
                    }

                    if (!keys.Any(x =>
                        lineStartTrimmed.StartsWithI(x + " ") || lineStartTrimmed.StartsWith(x + '\t')))
                    {
                        continue;
                    }

                    Match match = null;
                    foreach (var key in keys)
                    {
                        if (!lineStartTrimmed.StartsWithI(key)) continue;

                        // It's supposed to be finding a space after a key; this prevents it from finding the
                        // first space in the key itself if there is one.
                        var lineAfterKey = lineStartTrimmed.Remove(0, key.Length);
                        match = ReadmeLineScanFinalValueRegex.Match(lineAfterKey);
                        if (match.Success) break;
                    }

                    if (match == null || !match.Success) continue;

                    ret = match.Groups["Value"].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(ret)) return ret;

            ret = CleanupValue(ret);

            return ret;
        }

        private static string CleanupValue(string value)
        {
            var ret = value;

            ret = ret.TrimEnd();

            // Remove surrounding quotes
            if (ret[0] == '\"' && ret.Last() == '\"') ret = ret.Trim('\"');

            // Remove unpaired leading or trailing quotes
            if ((ret[0] == '\"' || ret.Last() == '\"') && ret.Count(x => x == '\"') == 1) ret = ret.Trim('\"');

            ret = ret.RemoveSurroundingParentheses();

            // Remove duplicate spaces
            ret = Regex.Replace(ret, @"\s{2,}", " ");

            if (ret.Contains('(') || ret.Contains(')'))
            {
                // Remove extraneous whitespace within parentheses
                ret = Regex.Replace(ret, @"\(\s+", "(");
                ret = Regex.Replace(ret, @"\s+\)", ")");

                // If there's stuff like "(this an incomplete sentence and" at the end, chop it right off
                if (ret.Count(x => x == '(') == 1 && !ret.Contains(')'))
                {
                    ret = ret.Substring(0, ret.LastIndexOf('(')).TrimEnd();
                }
            }

            return ret;
        }

        private string GetTitleFromNewGameStrFile(List<NameAndIndex> intrfaceDirFiles)
        {
            if (!intrfaceDirFiles.Any()) return null;
            var newGameStrFile = new NameAndIndex();

            char dsc = FmIsZip ? '/' : Path.DirectorySeparatorChar;

            if (intrfaceDirFiles.Any())
            {
                newGameStrFile =
                    intrfaceDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Intrface + dsc + "english" + dsc + FMFiles.NewGameStr))
                    ?? intrfaceDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Intrface + dsc + FMFiles.NewGameStr))
                    ?? intrfaceDirFiles.FirstOrDefault(x =>
                        x.Name.StartsWithI(FMDirs.Intrface + dsc) &&
                        x.Name.EndsWithI(dsc + FMFiles.NewGameStr));
            }

            if (newGameStrFile == null) return null;

            string[] lines = null;

            if (FmIsZip)
            {
                var e = Archive.Entries[newGameStrFile.Index];
                if (e != null)
                {
                    using (var es = e.Open())
                    {
                        lines = ReadAllLinesE(es, e.Length);
                    }
                }
            }
            else
            {
                lines = ReadAllLinesE(Path.Combine(FmWorkingPath, newGameStrFile.Name));
            }

            if (lines == null) return null;

            for (var i = 0; i < lines.Length; i++)
            {
                var lineT = lines[i].Trim();
                var match = NewGameStrTitleRegex.Match(lineT);
                if (match.Success)
                {
                    var title = match.Groups["Title"].Value.Trim();
                    if (string.IsNullOrEmpty(title)) continue;

                    // Do our best to ignore things that aren't titles
                    if ("{}-_:;!@#$%^&*()".All(x => title[0] != x) &&
                        !title.EqualsI("Play") && !title.EqualsI("Start") &&
                        !title.EqualsI("Begin") && !title.EqualsI("Begin...") &&
                        !title.EqualsI("skip training") &&
                        !title.StartsWithI("Let's go") && !title.StartsWithI("Let's rock this boat") &&
                        !title.StartsWithI("Play ") && !title.StartsWithI("Continue") &&
                        !title.StartsWithI("Start ") && !title.StartsWithI("Begin "))
                    {
                        return title;
                    }
                }
            }

            return null;
        }

        private static string CleanupTitle(string value)
        {
            // Some titles are clever and  A r e  W r i t t e n  L i k e  T h i s
            // But we want to leave titles that are supposed to be acronyms - ie, "U F O", "R G B"
            if (value.Contains(' ') &&
                !TitleAnyConsecutiveLettersRegex.Match(value).Success &&
                TitleContainsLowerCaseCharsRegex.Match(value).Success)
            {
                if (value.Contains("  "))
                {
                    var titleWords = value.Split(new[] { "  " }, StringSplitOptions.None);
                    for (var i = 0; i < titleWords.Length; i++)
                    {
                        titleWords[i] = titleWords[i].Replace(" ", "");
                    }

                    value = string.Join(" ", titleWords);
                }
                else
                {
                    value = value.Replace(" ", "");
                }
            }

            value = CleanupValue(value);

            return value;
        }

        private static string GetAuthorFromTopOfReadme(string[] linesArray, bool fmTitleStartsWithBy)
        {
            // Look for a "by [author]" in the first few lines. Looking for a line starting with "by" throughout
            // the whole text is asking for a cavalcade of false positives, hence why we only look near the top.
            var lines = linesArray.ToList();

            lines.RemoveAll(string.IsNullOrWhiteSpace);

            if (lines.Count < 2) return null;

            int linesToSearch = lines.Count >= 5 ? 5 : lines.Count;
            for (int i = 0; i < linesToSearch; i++)
            {
                // Preemptive check
                if (i == 0 && fmTitleStartsWithBy) continue;

                string lineT = lines[i].Trim();
                if (new[] { "By ", "By: " }.Any(x => lineT.StartsWithI(x)))
                {
                    string author = lineT.Substring(lineT.IndexOf(' ')).TrimStart();
                    if (!string.IsNullOrEmpty(author)) return author;
                }
                else
                {
                    var m = AuthorGeneralCopyrightRegex.Match(lineT);
                    if (!m.Success) continue;

                    string author = m.Groups["Author"].Value;

                    author = CleanupCopyrightAuthor(author);
                    if (!string.IsNullOrEmpty(author)) return author;
                }
            }

            return null;
        }

        private static string GetAuthorFromText(string text)
        {
            string author = null;

            for (int i = 0; i < AuthorRegexes.Length; i++)
            {
                var match = AuthorRegexes[i].Match(text);
                if (match.Success)
                {
                    author = match.Groups["Author"].Value;
                    break;
                }
            }

            return !string.IsNullOrEmpty(author) ? author : null;
        }

        private string GetAuthorFromCopyrightMessage()
        {
            string author = null;

            bool foundAuthor = false;

            foreach (var rf in ReadmeFiles.Where(x => !x.FileName.ExtIsHtml() && x.FileName.IsEnglishReadme()))
            {
                bool inCopyrightSection = false;
                bool pastFirstLineOfCopyrightSection = false;

                foreach (var line in rf.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    if (inCopyrightSection)
                    {
                        // This whole nonsense is just to support the use of @ as a copyright symbol (used by some
                        // Theker missions); we want to be very specific about when we decide that "@" means "©".
                        var m = !pastFirstLineOfCopyrightSection
                            ? AuthorGeneralCopyrightIncludeAtSymbolRegex.Match(line)
                            : AuthorGeneralCopyrightRegex.Match(line);
                        if (m.Success)
                        {
                            author = m.Groups["Author"].Value;
                            foundAuthor = true;
                            break;
                        }

                        pastFirstLineOfCopyrightSection = true;
                    }

                    var authorMatch = AuthorMissionCopyrightRegex.Match(line);
                    if (authorMatch.Success)
                    {
                        author = authorMatch.Groups["Author"].Value;
                        foundAuthor = true;
                        break;
                    }

                    var lineT = line.Trim('*').Trim('-').Trim();
                    if (lineT.EqualsI("Copyright Information") || lineT.EqualsI("Copyright"))
                    {
                        inCopyrightSection = true;
                    }
                }

                if (foundAuthor) break;
            }

            if (string.IsNullOrWhiteSpace(author)) return null;

            author = CleanupCopyrightAuthor(author);

            return author;
        }

        private static string CleanupCopyrightAuthor(string author)
        {
            author = author.Trim().RemoveSurroundingParentheses();

            var index = author.IndexOf(',');
            if (index > -1) author = author.Substring(0, index);

            index = author.IndexOf(". ", Ordinal);
            if (index > -1) author = author.Substring(0, index);

            var yearMatch = CopyrightAuthorYearRegex.Match(author);
            if (yearMatch.Success) author = author.Substring(0, yearMatch.Index);

            if ("!@#$%^&*".Any(x => author.Last() == x) &&
                author.ElementAt(author.Length - 2) == ' ')
            {
                author = author.Substring(0, author.Length - 2);
            }

            author = author.TrimEnd('.').Trim();

            return author;
        }

        private string GetVersion()
        {
            var version = GetValueFromReadme(SpecialLogic.Version, "Version");

            if (string.IsNullOrEmpty(version)) return null;

            Debug.WriteLine(@"GetVersion() top:");
            Debug.WriteLine(version);

            const string numbers = "0123456789.";
            if (numbers.Any(x => version[0] == x))
            {
                int indexSpace = version.IndexOf(' ');
                int indexTab = version.IndexOf('\t');

                int index = indexSpace > -1 && indexTab > -1
                    ? Math.Min(indexSpace, indexTab)
                    : Math.Max(indexSpace, indexTab);

                if (index > -1)
                {
                    version = version.Substring(0, index);
                }
            }
            else // Starts with non-numbers
            {
                // Find index of the first numeric character
                var match = VersionFirstNumberRegex.Match(version);
                if (match.Success)
                {
                    version = version.Substring(match.Index);

                    int indexSpace = version.IndexOf(' ');
                    int indexTab = version.IndexOf('\t');

                    int index = indexSpace > -1 && indexTab > -1
                        ? Math.Min(indexSpace, indexTab)
                        : Math.Max(indexSpace, indexTab);

                    if (index > -1)
                    {
                        version = version.Substring(0, index);
                    }
                }
            }

            Debug.WriteLine(@"GetVersion() bottom:");
            Debug.WriteLine(version);

            return version;
        }

        // TODO: Add all missing languages, and implement language detection for non-folder-specified FMs
        private string[] GetLanguages(List<NameAndIndex> baseDirFiles)
        {
            var langs = new List<string>();

            // Check multiple folders just to be sure
            foreach (var langDir in LanguageDirs)
            {
                if (FmIsZip)
                {
                    foreach (var lang in Languages)
                    {
                        langs.AddRange(
                            from e in Archive.Entries
                            let d = e.FullName.TrimEnd('/')
                            where d.StartsWithI(langDir) &&
                                  Regex.Match(d, @"/" + lang + @"( Language)?/", RegexOptions.IgnoreCase).Success &&
                                  Regex.Match(d, @"\..*$").Success
                            let dTrimmedStart = d.Substring(d.IndexOf('/') + 1)
                            select lang);
                    }
                }
                else
                {
                    if (!Directory.Exists(Path.Combine(FmWorkingPath, langDir))) continue;

                    var dirs = Directory.EnumerateDirectories(Path.Combine(FmWorkingPath, langDir), "*",
                        SearchOption.AllDirectories);

                    // TODO: Why am I doing this backwards?!
                    // TODO: The +" Language" thing is really ill-fitting for this, try and make it more like above
                    langs.AddRange(
                        from d in dirs
                        let dn = DirName(d)
                        // absolutely disgusting, but works
                        where (Languages.ContainsI(dn) ||
                               (dn.EndsWithI(" Language") &&
                                Languages.ContainsI(dn.Substring(0, dn.IndexOf(' '))))) &&
                              EnumFiles(d, "*", SearchOption.AllDirectories).Any()
                        select dn.Contains(' ')
                            ? dn.ToLowerInvariant().Substring(0, dn.IndexOf(' '))
                            : dn.ToLowerInvariant());
                }
            }

            if (!langs.ContainsI("english"))
            {
                langs.Add("english");
            }

            // Sometimes extra languages are in zip files inside the FM archive
            var zipFiles =
                from f in baseDirFiles
                where new[] { ".zip", ".7z", ".rar" }.Any(x => f.Name.ExtEqualsI(x))
                select f.Name.RemoveExtension();

            foreach (var fn in zipFiles)
            {
                langs.AddRange(
                    from lang in Languages
                    where fn.StartsWithI(lang)
                    select lang);

                // "Italiano" will be caught by StartsWithI("italian")

                // Extra logic to account for whatever-style naming
                if (fn.EqualsI("rus") ||
                    fn.EndsWithI("_ru") ||
                    fn.EndsWithI("_rus") ||
                    Regex.Match(fn, @"[a-z]+RUS$").Success ||
                    new[] { "RusPack", "RusText" }.Any(x => fn.ContainsI(x)))
                {
                    langs.Add("russian");
                }
                else if (fn.ContainsI("Francais"))
                {
                    langs.Add("french");
                }
                else if (new[] { "Deutsch", "Deutch" }.Any(x => fn.ContainsI(x)))
                {
                    langs.Add("german");
                }
                else if (fn.ContainsI("Espanol"))
                {
                    langs.Add("spanish");
                }
                else if (fn.ContainsI("Nederlands"))
                {
                    langs.Add("dutch");
                }
                else if (fn.EqualsI("huntext"))
                {
                    langs.Add("hungarian");
                }
            }

            if (langs.Any())
            {
                var langsD = langs.Distinct().ToArray();
                Array.Sort(langsD);
                return langsD;
            }
            else
            {
                return new[] { "english" };
            }
        }

        private Tuple<bool?, string>
        GetGameTypeAndEngine(List<NameAndIndex> usedMisFiles)
        {
            bool? retNewDarkRequired = null;
            string retGame = null;

            var misFile = Path.Combine(FmWorkingPath, usedMisFiles[0].Name);

            ZipArchiveEntry misFileZipArchiveEntry = null;
            if (FmIsZip)
            {
                misFileZipArchiveEntry = Archive.Entries[usedMisFiles[0].Index];
            }

            #region Check for SKYOBJVAR (determines OldDark/NewDark; determines game type for OldDark)

            // Note: DarkLoader looks for "SKYOBJVAR" and "MAPPARAM", but the latter only occurs in System
            // Shock 2 files. http://www.ttlg.com/forums/showthread.php?t=113389 Good to know!

            /*
            Note: The SKYOBJVAR check is the fastest way to tell if a mission is for Thief 1 or Thief 2, and
            for OldDark or NewDark. We check it first because it's fast. It will always tell us whether an FM
            is OldDark or NewDark, but for NewDark FMs it can't tell us what game the mission is for. In that
            case, we check for the string "RopeyArrow", and if we find it then it's for Thief 2, otherwise
            Thief 1. But that's slow so we only do it if all else fails.

            In 99% of cases, if the text "SKYOBJVAR" exists, it will be somewhere around 770, 3092, or 7200
            bytes in. To speed things up, we read the locations in order of commonness (770 = 80%, 7200 = 14%,
            3092 = 4%). This cuts the operation time down by a huge amount.
            Note: unless we're reading a zip, in which case we can't seek the stream so we just read the
            locations sequentially.

            Key:
                No SKYOBJVAR                       - OldDark Thief 1/G
                SKYOBJVAR at ~770                  - OldDark Thief 2
                SKYOBJVAR at ~3092 or ~7216        - NewDark, could be either T1/G or T2
                SKYOBJVAR at any other location *  - OldDark Thief2

            * We skip this check because only a handful of OldDark Thief 2 missions have SKYOBJVAR in a wacky
              location, and we don't want to try to guess where it is when we're about to do the much more
              reliable and nearly as fast RopeyArrow check anyway.
            */

            const int oldDarkThief2Location = 750;
            const int newDarkLocation1 = 7180;
            const int newDarkLocation2 = 3050;
            int[] locations = { oldDarkThief2Location, newDarkLocation1, newDarkLocation2 };

            // 750+100 = 850
            // (3050+100)-850 = 2300
            // ((7180+100)-2300)-850 = 4130
            int[] zipOffsets = { 850, 2300, 4130 };

            const int locationBytesToRead = 100;
            var foundAtNewDarkLocation = false;

            char[] zipBuf = null;
            var misAllChars = new char[locationBytesToRead];

            using (var sr = FmIsZip
                ? new StreamReader(misFileZipArchiveEntry.Open(), Encoding.ASCII, false, 1024, true)
                : new StreamReader(misFile, Encoding.ASCII, false, locationBytesToRead))
            {
                for (int i = 0; i < locations.Length; i++)
                {
                    if (FmIsZip)
                    {
                        zipBuf = new char[zipOffsets[i]];
                        sr.ReadBlock(zipBuf, 0, zipOffsets[i]);
                    }
                    else
                    {
                        sr.BaseStream.Position = locations[i];
                        sr.ReadBlock(misAllChars, 0, locationBytesToRead);
                    }

                    // We avoid string.Concat() in favor of directly searching char arrays, as that's WAY faster
                    if ((FmIsZip ? zipBuf : misAllChars).Contains(MisFileStrings.SkyObjVar))
                    {
                        // Zip reading is going to check the NewDark locations the other way round, but
                        // fortunately they're interchangeable in meaning so we don't have to do anything
                        if (locations[i] == newDarkLocation1 || locations[i] == newDarkLocation2)
                        {
                            retNewDarkRequired = true;
                            foundAtNewDarkLocation = true;
                            break;
                        }
                        else if (locations[i] == oldDarkThief2Location)
                        {
                            return Tuple.Create(
                                ScanOptions.ScanNewDarkRequired ? (bool?)false : null,
                                ScanOptions.ScanGameType ? Games.TMA : null);
                        }
                    }

                    // Necessary to get it to read from the correct location
                    sr.DiscardBufferedData();
                }
            }

            #endregion

            if (!foundAtNewDarkLocation) retNewDarkRequired = false;

            if (!ScanOptions.ScanGameType) return Tuple.Create(retNewDarkRequired, (string)null);

            #region Check for RopeyArrow (determines game type for both OldDark and NewDark)

            /*
            We couldn't determine the game type the fast way, so we're going to search the OBJ_MAP chunk for
            "RopeyArrow", which is a 100% reliable marker for Thief 2, whether OldDark or NewDark. This is
            actually perfectly fast for already-extracted FMs, but not so much for zips. With zips, we can
            only read a file entry forwards; we can't seek around. That's fine for SKYOBJVAR because it's only
            a few hundred bytes to a few K into the .mis file, so we can save a lot of time by not having to
            extract the whole thing into a MemoryStream. For the "RopeyArrow" string, though, we're out of
            luck: it can be absolutely anywhere, and the only way we can narrow its location down is to read
            the table of contents. Which is at the end of the .mis file, of course. So we'd be reading through
            95%+ of the file anyway, and then we'd have to reopen the stream and read through it again until
            we hit the right position. Faster just to copy it to a MemoryStream and seek through that.
            */
            long len = 0;
            if (FmIsZip) len = misFileZipArchiveEntry.Length;
            using (var misFileMemoryStream = new MemoryStream((int)len))
            {
                if (FmIsZip)
                {
                    using (var misFileZipStream = misFileZipArchiveEntry.Open())
                    {
                        misFileZipStream.CopyTo(misFileMemoryStream);
                        misFileMemoryStream.Position = 0;
                    }
                }

                using (var br = FmIsZip
                    ? new BinaryReader(misFileMemoryStream, Encoding.ASCII, leaveOpen: true)
                    : new BinaryReader(File.Open(misFile, FileMode.Open, FileAccess.Read), Encoding.ASCII,
                        leaveOpen: false))
                {
                    uint tocOffset = br.ReadUInt32();

                    br.BaseStream.Position = tocOffset;

                    var invCount = br.ReadUInt32();
                    for (int i = 0; i < invCount; i++)
                    {
                        var header = br.ReadChars(12);
                        var offset = br.ReadUInt32();
                        var length = br.ReadUInt32();

                        if (!header.Contains(MisFileStrings.ObjMap)) continue;

                        br.BaseStream.Position = offset;

                        var content = br.ReadChars((int)length);
                        retGame = content.Contains(MisFileStrings.RopeyArrow)
                            ? Games.TMA
                            : Games.TDP;
                        break;
                    }
                }
            }

            #endregion

            return Tuple.Create(retNewDarkRequired, retGame);
        }

        private static string GetNewDarkVersionFromText(string text)
        {
            string version = null;

            for (int i = 0; i < NewDarkVersionRegexes.Length; i++)
            {
                var match = NewDarkVersionRegexes[i].Match(text);
                if (match.Success)
                {
                    version = match.Groups["Version"].Value;
                    break;
                }
            }

            if (string.IsNullOrEmpty(version)) return null;

            var ndv = version.Trim('.');
            int index = ndv.IndexOf('.');
            if (index > -1 && ndv.Substring(index + 1).Length < 2)
            {
                ndv += "0";
            }

            // Anything lower than 1.19 is OldDark; and cut it off at 2.0 to prevent that durn old time-
            // travelling Zealot's Hollow from claiming it was made with "NewDark Version 2.1"
            var ndvF = float.Parse(ndv);
            return ndvF >= 1.19 && ndvF < 2.0 ? ndv : null;
        }

        private static void DeleteFmWorkingPath(string fmWorkingPath)
        {
            try
            {
                foreach (var d in Directory.EnumerateDirectories(fmWorkingPath, "*",
                    SearchOption.TopDirectoryOnly))
                {
                    Directory.Delete(d, true);
                }

                Directory.Delete(fmWorkingPath, true);
            }
            catch (Exception)
            {
                // Don't care
            }
        }

        #region Generic dir/file functions

        private IEnumerable<string>
        EnumFiles(string searchPattern, SearchOption searchOption)
        {
            return EnumFiles("", searchPattern, searchOption, checkDirExists: false);
        }

        private IEnumerable<string>
        EnumFiles(string path, string searchPattern, SearchOption searchOption, bool checkDirExists = true)
        {
            var fullDir = Path.Combine(FmWorkingPath, path);

            if (!checkDirExists || Directory.Exists(fullDir))
            {
                return Directory.EnumerateFiles(fullDir, searchPattern, searchOption);
            }

            return new List<string>();
        }

        private string DirName(string path)
        {
            if (FmIsZip)
            {
                path = path.Trim('/');
                return !path.Contains('/') ? path : path.Substring(path.LastIndexOf('/') + 1);
            }
            else
            {
                return new DirectoryInfo(Path.Combine(FmWorkingPath, path)).Name;
            }
        }

        #endregion
        public void Dispose()
        {
            ArchiveStream?.Dispose();
            Archive?.Dispose();
        }
    }
}
