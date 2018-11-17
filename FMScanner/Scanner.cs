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
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    public class Scanner : IDisposable
    {
        private Stopwatch OverallTimer { get; } = new Stopwatch();

        #region Properties

        #region Disposable

        private FileStream ArchiveStream { get; set; }
        private ZipArchive Archive { get; set; }

        #endregion

        private char dsc { get; set; }

        private ScanOptions ScanOptions { get; set; } = new ScanOptions();

        private bool FmIsZip { get; set; }

        private string ArchivePath { get; set; }

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

        #region Scan synchronous

        public ScannedFMData
        Scan(string mission, string tempPath)
        {
            return ScanMany(new List<string> { mission }, tempPath, this.ScanOptions, null,
                    CancellationToken.None)[0];
        }

        public ScannedFMData
        Scan(string mission, string tempPath, ScanOptions scanOptions)
        {
            return ScanMany(new List<string> { mission }, tempPath, scanOptions, null,
                    CancellationToken.None)[0];
        }

        // Debug - scan on UI thread so breaks will actually break where they're supposed to
#if DEBUG
        public List<ScannedFMData>
            Scan(List<string> missions, string tempPath, ScanOptions scanOptions,
                IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            return ScanMany(missions, tempPath, scanOptions, null, CancellationToken.None);
        }
#endif

        #endregion

        #region Scan asynchronous

        public async Task<List<ScannedFMData>>
        ScanAsync(List<string> missions, string tempPath)
        {
            return await Task.Run(() =>
                ScanMany(missions, tempPath, this.ScanOptions, null, CancellationToken.None));
        }

        public async Task<List<ScannedFMData>>
        ScanAsync(List<string> missions, string tempPath, ScanOptions scanOptions)
        {
            return await Task.Run(() =>
                ScanMany(missions, tempPath, scanOptions, null, CancellationToken.None));
        }

        public async Task<List<ScannedFMData>>
        ScanAsync(List<string> missions, string tempPath, IProgress<ProgressReport> progress,
            CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
                ScanMany(missions, tempPath, this.ScanOptions, progress, cancellationToken));
        }

        public async Task<List<ScannedFMData>>
        ScanAsync(List<string> missions, string tempPath, ScanOptions scanOptions,
            IProgress<ProgressReport> progress, CancellationToken cancellationToken)
        {
            return await Task.Run(() =>
                ScanMany(missions, tempPath, scanOptions, progress, cancellationToken));
        }

        #endregion

        private List<ScannedFMData>
        ScanMany(List<string> missions, string tempPath, ScanOptions scanOptions,
            IProgress<ProgressReport> progress, CancellationToken cancellationToken)
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

            // Init and dispose rtfBox here to avoid cross-thread exceptions.
            // For performance, we only have one instance and we just change its content as needed.
            using (var rtfBox = new RichTextBox())
            {
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

                    scannedFMDataList.Add(ScanCurrentFM(rtfBox));

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
            }

            return scannedFMDataList;
        }

        private ScannedFMData ScanCurrentFM(RichTextBox rtfBox)
        {
            OverallTimer.Restart();

            dsc = FmIsZip ? '/' : Path.DirectorySeparatorChar;

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
            var booksDirFiles = new List<NameAndIndex>();

            var fmData = new ScannedFMData();

            #region Cache FM data

            {
                var success =
                    ReadAndCacheFMData(fmData, baseDirFiles, misFiles, usedMisFiles, stringsDirFiles,
                        intrfaceDirFiles, booksDirFiles);

                if (!success)
                {
                    if (fmIsSevenZip) DeleteFmWorkingPath(FmWorkingPath);
                    return null;
                }
            }

            #endregion

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
                    if (ScanOptions.ScanTitle) SetOrAddTitle(t.Title);
                    if (ScanOptions.ScanAuthor) fmData.Author = t.Author;
                    fmData.Version = t.Version;
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

            ReadAndCacheReadmeFiles(baseDirFiles, rtfBox);

            #region Title and IncludedMissions

            if (ScanOptions.ScanTitle || ScanOptions.ScanCampaignMissionNames)
            {
                var (titleFrom0, titleFromN, cNames) = GetMissionNames(stringsDirFiles, misFiles, usedMisFiles);
                if (ScanOptions.ScanTitle)
                {
                    SetOrAddTitle(titleFrom0);
                    SetOrAddTitle(titleFromN);
                }

                if (ScanOptions.ScanCampaignMissionNames && cNames != null && cNames.Length > 0)
                {
                    for (int i = 0; i < cNames.Length; i++) cNames[i] = CleanupTitle(cNames[i]);
                    fmData.IncludedMissions = cNames;
                }
            }

            if (ScanOptions.ScanTitle)
            {
                SetOrAddTitle(
                    GetValueFromReadme(SpecialLogic.Title, "Title of the Mission", "Title of the mission",
                        "Title", "Mission Title", "Mission title", "Mission Name", "Mission name", "Level Name",
                        "Level name", "Mission:", "Mission ", "Campaign Title", "Campaign title",
                        "The name of Mission:"));

                SetOrAddTitle(GetTitleFromNewGameStrFile(intrfaceDirFiles));
            }

            #endregion

            #region Author

            if (ScanOptions.ScanAuthor)
            {
                if (fmData.Author.IsEmpty())
                {
                    // TODO: Do I want to check AlternateTitles for StartsWithI("By ") as well?
                    var author =
                        GetValueFromReadme(SpecialLogic.Author, fmData.Title.StartsWithI("By "),
                            "Author", "Authors", "Autor",
                            "Created by", "Devised by", "Designed by", "Author=", "Made by",
                            "FM Author", "Mission Author", "Mission author", "The author:",
                            "author:");

                    fmData.Author = CleanupValue(author);
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
                fmData.Languages = GetLanguages(baseDirFiles, booksDirFiles, intrfaceDirFiles, stringsDirFiles);
            }

            #region NewDark/GameType checks

            if (ScanOptions.ScanNewDarkRequired || ScanOptions.ScanGameType)
            {
                var t = GetGameTypeAndEngine(baseDirFiles, usedMisFiles);
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

        private bool ReadAndCacheFMData(ScannedFMData fmd, List<NameAndIndex> baseDirFiles,
            List<NameAndIndex> misFiles, List<NameAndIndex> usedMisFiles, List<NameAndIndex> stringsDirFiles,
            List<NameAndIndex> intrfaceDirFiles, List<NameAndIndex> booksDirFiles)
        {
            #region Add BaseDirFiles

            // This is split out because of weird semantics with if(this && that) vs nested ifs (required in
            // order to have a var in the middle to avoid multiple LastIndexOf calls).
            bool MapFileExists(string path)
            {
                if (path.StartsWithI(FMDirs.Intrface + '/') &&
                    path.CountChars('/') >= 2)
                {
                    var lsi = path.LastIndexOf('/');
                    if (path.Length > lsi + 5 &&
                        path.Substring(lsi + 1, 5).EqualsI("page0") &&
                        path.LastIndexOf('.') > lsi)
                    {
                        return true;
                    }
                }

                return false;
            }

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
                            // Fallthrough so ScanCustomResources can use it
                        }
                        else if (e.FullName.StartsWithI(FMDirs.Strings + '/'))
                        {
                            stringsDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                            continue;
                        }
                        else if (e.FullName.StartsWithI(FMDirs.Intrface + '/'))
                        {
                            intrfaceDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                            // Fallthrough so ScanCustomResources can use it
                        }
                        else if (e.FullName.StartsWithI(FMDirs.Books + '/'))
                        {
                            booksDirFiles.Add(new NameAndIndex { Name = e.FullName, Index = i });
                            continue;
                        }

                        // Inlined for performance. We cut the time roughly in half by doing this.
                        if (ScanOptions.ScanCustomResources)
                        {
                            if (fmd.HasAutomap == null &&
                                     e.FullName.StartsWithI(FMDirs.Intrface + '/') &&
                                     e.FullName.CountChars('/') >= 2 &&
                                     e.FullName.EndsWithI("ra.bin"))
                            {
                                fmd.HasAutomap = true;
                                // Definitely a clever deduction, definitely not a sneaky hack for GatB-T2
                                fmd.HasMap = true;
                            }
                            else if (fmd.HasMap == null && MapFileExists(e.FullName))
                            {
                                fmd.HasMap = true;
                            }
                            else if (fmd.HasCustomMotions == null &&
                                     e.FullName.StartsWithI(FMDirs.Motions + '/') &&
                                     MotionFileExtensions.Any(e.FullName.EndsWithI))
                            {
                                fmd.HasCustomMotions = true;
                            }
                            else if (fmd.HasMovies == null &&
                                     e.FullName.StartsWithI(FMDirs.Movies + '/') &&
                                     e.FullName.HasFileExtension())
                            {
                                fmd.HasMovies = true;
                            }
                            else if (fmd.HasCustomTextures == null &&
                                     e.FullName.StartsWithI(FMDirs.Fam + '/') &&
                                     ImageFileExtensions.Any(e.FullName.EndsWithI))
                            {
                                fmd.HasCustomTextures = true;
                            }
                            else if (fmd.HasCustomObjects == null &&
                                     e.FullName.StartsWithI(FMDirs.Obj + '/') &&
                                     e.FullName.EndsWithI(".bin"))
                            {
                                fmd.HasCustomObjects = true;
                            }
                            else if (fmd.HasCustomCreatures == null &&
                                     e.FullName.StartsWithI(FMDirs.Mesh + '/') &&
                                     e.FullName.EndsWithI(".bin"))
                            {
                                fmd.HasCustomCreatures = true;
                            }
                            else if (fmd.HasCustomScripts == null &&
                                     (!e.FullName.Contains('/') &&
                                      ScriptFileExtensions.Any(e.FullName.EndsWithI)) ||
                                     (e.FullName.StartsWithI(FMDirs.Scripts + '/') &&
                                      e.FullName.HasFileExtension()))
                            {
                                fmd.HasCustomScripts = true;
                            }
                            else if (fmd.HasCustomSounds == null &&
                                     e.FullName.StartsWithI(FMDirs.Snd + '/') &&
                                     e.FullName.HasFileExtension())
                            {
                                fmd.HasCustomSounds = true;
                            }
                            else if (fmd.HasCustomSubtitles == null &&
                                     e.FullName.StartsWithI(FMDirs.Subtitles + '/') &&
                                     e.FullName.EndsWithI(".sub"))
                            {
                                fmd.HasCustomSubtitles = true;
                            }
                        }
                    }

                    if (ScanOptions.ScanCustomResources)
                    {
                        if (fmd.HasMap == null) fmd.HasMap = false;
                        if (fmd.HasAutomap == null) fmd.HasAutomap = false;
                        if (fmd.HasCustomMotions == null) fmd.HasCustomMotions = false;
                        if (fmd.HasMovies == null) fmd.HasMovies = false;
                        if (fmd.HasCustomTextures == null) fmd.HasCustomTextures = false;
                        if (fmd.HasCustomObjects == null) fmd.HasCustomObjects = false;
                        if (fmd.HasCustomCreatures == null) fmd.HasCustomCreatures = false;
                        if (fmd.HasCustomScripts == null) fmd.HasCustomScripts = false;
                        if (fmd.HasCustomSounds == null) fmd.HasCustomSounds = false;
                        if (fmd.HasCustomSubtitles == null) fmd.HasCustomSubtitles = false;
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

                    foreach (var f in EnumFiles(FMDirs.Books, "*", SearchOption.AllDirectories))
                    {
                        booksDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length + 1) });
                    }

                    // Call this here just so both calls are in one place
                    if (ScanOptions.ScanCustomResources)
                    {
                        CheckForCustomResources_FmIsDir(fmd, baseDirFiles);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.WriteLine(e);
                return false;
            }

            if (baseDirFiles.Count == 0) return false;

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

            if (misFiles.Count == 0) return false;

            #endregion

            #region Cache list of used .mis files

            NameAndIndex missFlag = null;

            if (stringsDirFiles.Count > 0)
            {
                // I don't remember if I need to search in this exact order, so uh... not rockin' the boat.
                missFlag =
                    stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Strings + dsc + FMFiles.MissFlag))
                    ?? stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.Strings + dsc + "english" + dsc + FMFiles.MissFlag))
                    ?? stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EndsWithI(dsc + FMFiles.MissFlag));
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
                    var mfNoExt = mf.Name.RemoveExtension();
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
            if (usedMisFiles.Count == 0) usedMisFiles.AddRange(misFiles);

            #endregion

            return true;
        }

        private void CheckForCustomResources_FmIsDir(ScannedFMData scannedFMData, List<NameAndIndex> baseDirFiles)
        {
            Debug.Assert(!FmIsZip, nameof(CheckForCustomResources_FmIsDir) +
                                   ": FmIsZip should be false, but is true.");

            var baseDirFolders = (
                from f in Directory.EnumerateDirectories(FmWorkingPath, "*",
                    SearchOption.TopDirectoryOnly)
                select DirName(f)).ToArray();

            scannedFMData.HasMap =
                baseDirFolders.ContainsI(FMDirs.Intrface) &&
                FastIO.FilesExistSearchAllSkipTop(Combine(FmWorkingPath, FMDirs.Intrface), "page0*.*");

            scannedFMData.HasAutomap =
                baseDirFolders.ContainsI(FMDirs.Intrface) &&
                FastIO.FilesExistSearchAllSkipTop(Combine(FmWorkingPath, FMDirs.Intrface), "*ra.bin");

            // Definitely a clever deduction, definitely not a sneaky hack for GatB-T2
            if (scannedFMData.HasAutomap == true) scannedFMData.HasMap = true;

            scannedFMData.HasCustomMotions =
                baseDirFolders.ContainsI(FMDirs.Motions) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Motions),
                    MotionFilePatterns);

            scannedFMData.HasMovies =
                baseDirFolders.ContainsI(FMDirs.Movies) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Movies), "*");

            scannedFMData.HasCustomTextures =
                baseDirFolders.ContainsI(FMDirs.Fam) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Fam),
                    ImageFilePatterns);

            scannedFMData.HasCustomObjects =
                baseDirFolders.ContainsI(FMDirs.Obj) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Obj), "*.bin");

            scannedFMData.HasCustomCreatures =
                baseDirFolders.ContainsI(FMDirs.Mesh) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Mesh), "*.bin");

            scannedFMData.HasCustomScripts =
                baseDirFiles.Any(x => ScriptFileExtensions.ContainsI(GetExtension(x.Name))) ||
                (baseDirFolders.ContainsI(FMDirs.Scripts) &&
                 FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Scripts), "*"));

            scannedFMData.HasCustomSounds =
                baseDirFolders.ContainsI(FMDirs.Snd) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Snd), "*");

            scannedFMData.HasCustomSubtitles =
                baseDirFolders.ContainsI(FMDirs.Subtitles) &&
                FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Subtitles), "*.sub");
        }

        private (string Title, string Author, string Version)
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

            return (title, author, version);
        }

        private (string Title, string Author, string Description, string LastUpdateDate)
        ReadFmIni(NameAndIndex file)
        {
            var ret = (Title: (string)null, Author: (string)null, Description: (string)null,
                LastUpdateDate: (string)null);

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

                if (string.IsNullOrEmpty(iniText)) return (null, null, null, null);

                using (var sr = new StringReader(iniText))
                {
                    ini.Load(sr);
                }

                fmIni = ini.Sections[0].Deserialize<FMIniData>();

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
                var tags = ini.Sections[0].Keys["Tags"];
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
                    ret.Author = authorString;
                }
            }

            #endregion

            if (ScanOptions.ScanTitle) ret.Title = fmIni.NiceName;
            ret.LastUpdateDate = fmIni.ReleaseDate;
            ret.Description = fmIni.Descr;

            /*
               Notes:
                - fm.ini can specify a readme file, but it may not be the one we're looking for, as far as
                  detecting values goes. Reading all .txt and .rtf files is slightly slower but more accurate.

                - Although fm.ini wasn't used before NewDark, its presence doesn't necessarily mean the mission
                  is NewDark-only. Sturmdrang Peak has it but doesn't require NewDark, for instance.
            */

            return ret;
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

                        if (RtfTags.Bytes10.SequenceEqual(RtfTags.shppict) ||
                            RtfTags.Bytes10.SequenceEqual(RtfTags.objdata) ||
                            RtfTags.Bytes11.SequenceEqual(RtfTags.nonshppict) ||
                            RtfTags.Bytes5.SequenceEqual(RtfTags.pict))
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

        private void ReadAndCacheReadmeFiles(List<NameAndIndex> baseDirFiles, RichTextBox rtfBox)
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

                    // Saw one ".rtf" that was actually a plaintext file, and one vice versa. So detect by
                    // header alone.
                    var rtfHeader = new char[6];
                    using (var sr = FmIsZip
                        ? new StreamReader(readmeStream, Encoding.ASCII, false, 6, true)
                        : new StreamReader(readmeFileOnDisk, Encoding.ASCII, false, 6))
                    {
                        sr.ReadBlock(rtfHeader, 0, 6);
                    }
                    if (FmIsZip) readmeStream.Position = 0;

                    if (string.Concat(rtfHeader).EqualsI(@"{\rtf1"))
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
                            var last = ReadmeFiles[ReadmeFiles.Count - 1];
                            last.Lines = rtfBox.Lines;
                            last.Text = rtfBox.Text;
                        }
                    }
                    else
                    {
                        var last = ReadmeFiles[ReadmeFiles.Count - 1];
                        last.Lines = FmIsZip
                            ? ReadAllLinesE(readmeStream, readmeLength)
                            : ReadAllLinesE(readmeFileOnDisk);
                        last.Text = string.Join("\r\n", last.Lines);
                    }
                }
                finally
                {
                    readmeStream?.Dispose();
                }
            }
        }

        private List<string> GetTitlesStrLines(List<NameAndIndex> stringsDirFiles)
        {
            string[] titlesStrLines = null;

            #region Read title(s).str file

            // Do not change search order: strings/english, strings, strings/[any other language]
            var titlesStrDirs = new List<string>
            {
                FMDirs.Strings + dsc + "english" + dsc + FMFiles.TitlesStr,
                FMDirs.Strings + dsc + "english" + dsc + FMFiles.TitleStr,
                FMDirs.Strings + dsc + FMFiles.TitlesStr,
                FMDirs.Strings + dsc + FMFiles.TitleStr
            };
            foreach (var lang in Languages)
            {
                if (lang == "english") continue;

                titlesStrDirs.Add(FMDirs.Strings + dsc + lang + dsc + FMFiles.TitlesStr);
                titlesStrDirs.Add(FMDirs.Strings + dsc + lang + dsc + FMFiles.TitleStr);
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
                    using (var es = e.Open())
                    {
                        titlesStrLines = ReadAllLinesE(es, e.Length);
                    }
                }
                else
                {
                    titlesStrLines = ReadAllLinesE(titlesFile.Name);
                }

                break;
            }

            #endregion

            if (titlesStrLines == null || titlesStrLines.Length == 0) return null;

            #region Filter titlesStrLines

            // There's a way to do this with an IEqualityComparer, but no, for reasons
            var tfLinesD = new List<string>(titlesStrLines.Length);
            {
                for (var i = 0; i < titlesStrLines.Length; i++)
                {
                    // Note: the Trim() is important, don't remove it
                    var line = titlesStrLines[i].Trim();
                    if (!string.IsNullOrEmpty(line) &&
                        line.Contains(':') &&
                        line.CountChars('\"') > 1 &&
                        line.StartsWithI("title_") &&
                        !tfLinesD.Any(x => x.StartsWithI(line.Substring(0, line.IndexOf(':')))))
                    {
                        tfLinesD.Add(line);
                    }
                }
            }

            tfLinesD.Sort(new TitlesStrNaturalNumericSort());

            #endregion

            return tfLinesD;
        }

        private (string TitleFrom0, string TitleFromNumbered, string[] CampaignMissionNames)
        GetMissionNames(List<NameAndIndex> stringsDirFiles, List<NameAndIndex> misFiles, List<NameAndIndex> usedMisFiles)
        {
            var titlesStrLines = GetTitlesStrLines(stringsDirFiles);
            if (titlesStrLines == null || titlesStrLines.Count == 0) return (null, null, null);

            var ret =
                (TitleFrom0: (string)null,
                TitleFromNumbered: (string)null,
                CampaignMissionNames: (string[])null);

            string ExtractFromQuotedSection(string line)
            {
                int i;
                return line.Substring(i = line.IndexOf('\"') + 1, line.IndexOf('\"', i) - i);
            }

            var titles = new List<string>(titlesStrLines.Count);
            for (int lineIndex = 0; lineIndex < titlesStrLines.Count; lineIndex++)
            {
                string titleNum = null;
                string title = null;
                for (int umfIndex = 0; umfIndex < usedMisFiles.Count; umfIndex++)
                {
                    var line = titlesStrLines[lineIndex];
                    {
                        int i;
                        titleNum = line.Substring(i = line.IndexOf('_') + 1, line.IndexOf(':') - i).Trim();
                    }
                    if (titleNum == "0")
                    {
                        ret.TitleFrom0 = ExtractFromQuotedSection(line);
                    }

                    title = ExtractFromQuotedSection(line);
                    if (string.IsNullOrEmpty(title)) continue;

                    var umfNoExt = usedMisFiles[umfIndex].Name.RemoveExtension();
                    if (umfNoExt != null && umfNoExt.StartsWithI("miss") && umfNoExt.Length > 4 &&
                        ScanOptions.ScanCampaignMissionNames &&
                        titleNum == umfNoExt.Substring(4))
                    {
                        titles.Add(title);
                    }
                }

                if (ScanOptions.ScanTitle &&
                    ret.TitleFromNumbered.IsEmpty() &&
                    lineIndex == titlesStrLines.Count - 1 &&
                    !string.IsNullOrEmpty(titleNum) &&
                    !string.IsNullOrEmpty(title) &&
                    !usedMisFiles.Any(x => x.Name.ContainsI("miss" + titleNum + ".mis")) &&
                    misFiles.Any(x => x.Name.ContainsI("miss" + titleNum + ".mis")))
                {
                    ret.TitleFromNumbered = title;
                    if (!ScanOptions.ScanCampaignMissionNames) break;
                }
            }

            if (titles.Count > 0)
            {
                if (ScanOptions.ScanTitle && titles.Count == 1)
                {
                    ret.TitleFromNumbered = titles[0];
                }
                else if (ScanOptions.ScanCampaignMissionNames)
                {
                    ret.CampaignMissionNames = titles.ToArray();
                }
            }

            return ret;
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
            for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                var lineStartTrimmed = line.TrimStart();

                #region Excludes

                switch (specialLogic)
                {
                    // I can't believe fallthrough is actually useful (for visual purposes only, but still!)
                    case SpecialLogic.Title when
                        lineStartTrimmed.StartsWithI("Title & Description") ||
                        lineStartTrimmed.StartsWithGL("Title screen"):
                    case SpecialLogic.Version when
                        lineStartTrimmed.StartsWithI("Version History") ||
                        lineStartTrimmed.ContainsI("NewDark") ||
                        lineStartTrimmed.ContainsI("64 Cubed") ||
                        VersionExclude1Regex.Match(lineStartTrimmed).Success:
                    case SpecialLogic.Author when
                        lineStartTrimmed.StartsWithI("Authors note"):
                        continue;
                }

                #endregion

                bool lineStartsWithKey = false;
                bool lineStartsWithKeyAndSeparatorChar = false;
                for (var i = 0; i < keys.Length; i++)
                {
                    var x = keys[i];

                    // Either in given case or in all caps, but not in lowercase, because that's given me at
                    // least one false positive
                    if (lineStartTrimmed.StartsWithGU(x))
                    {
                        lineStartsWithKey = true;
                        // Regex perf: fast enough not to worry about it
                        if (Regex.Match(lineStartTrimmed, @"^" + x + @"\s*(:|-)", RegexOptions.IgnoreCase)
                            .Success)
                        {
                            lineStartsWithKeyAndSeparatorChar = true;
                            break;
                        }
                    }
                }
                if (!lineStartsWithKey) continue;

                if (lineStartsWithKeyAndSeparatorChar)
                {
                    int indexColon = lineStartTrimmed.IndexOf(':');
                    int indexDash = lineStartTrimmed.IndexOf('-');

                    int index = indexColon > -1 && indexDash > -1
                        ? Math.Min(indexColon, indexDash)
                        : Math.Max(indexColon, indexDash);

                    var finalValue = lineStartTrimmed.Substring(index + 1).Trim();
                    if (!string.IsNullOrEmpty(finalValue)) return finalValue;
                }
                else
                {
                    // Don't detect "Version "; too many false positives
                    // TODO: Can probably remove this check and then just sort out any false positives in
                    // TODO: GetVersion()
                    if (specialLogic == SpecialLogic.Version) continue;

                    for (var i = 0; i < keys.Length; i++)
                    {
                        var key = keys[i];
                        if (!lineStartTrimmed.StartsWithI(key)) continue;

                        // It's supposed to be finding a space after a key; this prevents it from finding the
                        // first space in the key itself if there is one.
                        var lineAfterKey = lineStartTrimmed.Remove(0, key.Length);

                        if (!string.IsNullOrEmpty(lineAfterKey) &&
                            (lineAfterKey[0] == ' ' || lineAfterKey[0] == '\t'))
                        {
                            var finalValue = lineAfterKey.TrimStart();
                            if (!string.IsNullOrEmpty(finalValue)) return finalValue;
                        }
                    }
                }
            }

            return null;
        }

        private static string CleanupValue(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            var ret = value.TrimEnd();

            // Remove surrounding quotes
            if (ret[0] == '\"' && ret[ret.Length - 1] == '\"') ret = ret.Trim('\"');

            // Remove unpaired leading or trailing quotes
            if (ret.CountChars('\"') == 1)
            {
                if (ret[0] == '\"')
                {
                    ret = ret.Substring(1);
                }
                else if (ret[ret.Length - 1] == '\"')
                {
                    ret = ret.Substring(0, ret.Length - 1);
                }
            }

            // Remove duplicate spaces
            ret = Regex.Replace(ret, @"\s{2,}", " ");
            ret = ret.Replace('\t', ' ');

            #region Parentheses

            ret = ret.RemoveSurroundingParentheses();

            var containsOpenParen = ret.Contains('(');
            var containsCloseParen = ret.Contains(')');

            // Remove extraneous whitespace within parentheses
            if (containsOpenParen) ret = ret.Replace("( ", "(");
            if (containsCloseParen) ret = ret.Replace(" )", ")");

            // If there's stuff like "(this an incomplete sentence and" at the end, chop it right off
            if (containsOpenParen && ret.CountChars('(') == 1 && !containsCloseParen)
            {
                ret = ret.Substring(0, ret.LastIndexOf('(')).TrimEnd();
            }

            #endregion

            return ret;
        }

        private string GetTitleFromNewGameStrFile(List<NameAndIndex> intrfaceDirFiles)
        {
            if (intrfaceDirFiles.Count == 0) return null;
            var newGameStrFile = new NameAndIndex();

            if (intrfaceDirFiles.Count > 0)
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

            string[] lines;

            if (FmIsZip)
            {
                var e = Archive.Entries[newGameStrFile.Index];
                using (var es = e.Open()) lines = ReadAllLinesE(es, e.Length);
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
            if (string.IsNullOrEmpty(value)) return value;

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

                var lineT = lines[i].Trim();
                if (lineT.StartsWithI("By ") || lineT.StartsWithI("By: "))
                {
                    var author = lineT.Substring(lineT.IndexOf(' ')).TrimStart();
                    if (!string.IsNullOrEmpty(author)) return author;
                }
                else
                {
                    var m = AuthorGeneralCopyrightRegex.Match(lineT);
                    if (!m.Success) continue;

                    var author = CleanupCopyrightAuthor(m.Groups["Author"].Value);
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

            if ("!@#$%^&*".Any(x => author[author.Length - 1] == x) &&
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
        private string[] GetLanguages(List<NameAndIndex> baseDirFiles, List<NameAndIndex> booksDirFiles,
            List<NameAndIndex> intrfaceDirFiles, List<NameAndIndex> stringsDirFiles)
        {
            var langs = new List<string>();

            // Some code dupe, but I feel better about this overall than intertwining them more tightly
            for (var dirIndex = 0; dirIndex < 3; dirIndex++)
            {
                if (FmIsZip)
                {
                    var dirFiles =
                        dirIndex == 0 ? booksDirFiles : dirIndex == 1 ? intrfaceDirFiles : stringsDirFiles;

                    for (var langIndex = 0; langIndex < Languages.Length; langIndex++)
                    {
                        var lang = Languages[langIndex];
                        for (var dfIndex = 0; dfIndex < dirFiles.Count; dfIndex++)
                        {
                            var df = dirFiles[dfIndex];
                            if (df.Name.HasFileExtension() &&
                                (df.Name.ContainsI('/' + lang + '/') ||
                                 df.Name.ContainsI('/' + lang + " Language/")))
                            {
                                langs.Add(lang);
                            }
                        }
                    }
                }
                else
                {
                    var langDir = LanguageDirs[dirIndex];

                    if (!Directory.Exists(Path.Combine(FmWorkingPath, langDir))) continue;

                    var dirFiles = Directory.EnumerateFiles(Path.Combine(FmWorkingPath, langDir), "*",
                        SearchOption.AllDirectories).ToArray();

                    for (var langIndex = 0; langIndex < Languages.Length; langIndex++)
                    {
                        var lang = Languages[langIndex];
                        for (var dfIndex = 0; dfIndex < dirFiles.Length; dfIndex++)
                        {
                            var df = dirFiles[dfIndex];
                            if (df.LastIndexOf('.') > df.LastIndexOf('\\') &&
                                (df.ContainsI('\\' + lang + '\\') ||
                                 df.ContainsI('\\' + lang + @" Language\")))
                            {
                                langs.Add(lang);
                            }
                        }
                    }
                }
            }

            if (!langs.ContainsI("english")) langs.Add("english");

            // Sometimes extra languages are in zip files inside the FM archive
            for (var i = 0; i < baseDirFiles.Count; i++)
            {
                var fn = baseDirFiles[i].Name;
                if (!fn.ExtEqualsI(".zip") && !fn.ExtEqualsI(".7z") && !fn.ExtEqualsI(".rar"))
                {
                    continue;
                }

                fn = fn.RemoveExtension();

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
                    fn.ContainsI("RusPack") || fn.ContainsI("RusText"))
                {
                    langs.Add("russian");
                }
                else if (fn.ContainsI("Francais"))
                {
                    langs.Add("french");
                }
                else if (fn.ContainsI("Deutsch") || fn.ContainsI("Deutch"))
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

            if (langs.Count > 0)
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

        private (bool? NewDarkRequired, string Game)
        GetGameTypeAndEngine(List<NameAndIndex> baseDirFiles, List<NameAndIndex> usedMisFiles)
        {
            var ret = (NewDarkRequired: (bool?)null, Game: (string)null);

            #region Choose smallest .gam file

            var gamFiles = baseDirFiles.Where(x => x.Name.ExtEqualsI(".gam")).ToArray();
            var gamFileExists = gamFiles.Length > 0;

            var gamSizeList = new List<(string Name, int Index, long Size)>();
            NameAndIndex smallestGamFile = null;

            if (gamFileExists)
            {
                if (gamFiles.Length == 1)
                {
                    smallestGamFile = gamFiles[0];
                }
                else
                {
                    foreach (var gam in gamFiles)
                    {
                        gamSizeList.Add((gam.Name, gam.Index,
                            FmIsZip
                                ? Archive.Entries[gam.Index].Length
                                : new FileInfo(Path.Combine(FmWorkingPath, gam.Name)).Length));
                    }

                    var gamToUse = gamSizeList.OrderBy(x => x.Size).First();
                    smallestGamFile = new NameAndIndex { Name = gamToUse.Name, Index = gamToUse.Index };
                }
            }

            #endregion

            #region Choose smallest .mis file

            var misSizeList = new List<(string Name, int Index, long Size)>();
            NameAndIndex smallestUsedMisFile;

            if (usedMisFiles.Count == 1)
            {
                smallestUsedMisFile = usedMisFiles[0];
            }
            else
            {
                foreach (var mis in usedMisFiles)
                {
                    misSizeList.Add((mis.Name, mis.Index,
                        FmIsZip
                            ? Archive.Entries[mis.Index].Length
                            : new FileInfo(Path.Combine(FmWorkingPath, mis.Name)).Length));
                }

                var misToUse = misSizeList.OrderBy(x => x.Size).First();
                smallestUsedMisFile = new NameAndIndex { Name = misToUse.Name, Index = misToUse.Index };
            }

            #endregion

            #region Setup

            ZipArchiveEntry gamFileZipEntry = null;
            ZipArchiveEntry misFileZipEntry = null;

            string misFileOnDisk = null;

            if (FmIsZip)
            {
                if (gamFileExists) gamFileZipEntry = Archive.Entries[smallestGamFile.Index];
                misFileZipEntry = Archive.Entries[smallestUsedMisFile.Index];
            }
            else
            {
                misFileOnDisk = Path.Combine(FmWorkingPath, smallestUsedMisFile.Name);
            }

            #endregion

            #region Check for SKYOBJVAR in .mis (determines OldDark/NewDark; determines game type for OldDark)

            /*
             SKYOBJVAR location key:
                 No SKYOBJVAR           - OldDark Thief 1/G
                 ~770                   - OldDark Thief 2                        Commonness: ~80%
                 ~7216                  - NewDark, could be either T1/G or T2    Commonness: ~14%
                 ~3092                  - NewDark, could be either T1/G or T2    Commonness: ~4%
                 Any other location*    - OldDark Thief2

             * We skip this check because only a handful of OldDark Thief 2 missions have SKYOBJVAR in a wacky
               location, and it's faster and more reliable to simply carry on with the secondary check than to
               try to guess where SKYOBJVAR is in this case.
            */

            // For folder scans, we can seek to these positions directly, but for zip scans, we have to read
            // through the stream sequentially until we hit each one.
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
            var foundAtOldDarkThief2Location = false;

            char[] zipBuf = null;
            var dirBuf = new char[locationBytesToRead];

            using (var sr = FmIsZip
                ? new BinaryReader(misFileZipEntry.Open(), Encoding.ASCII, false)
                : new BinaryReader(new FileStream(misFileOnDisk, FileMode.Open, FileAccess.Read), Encoding.ASCII,
                    false))
            {
                for (int i = 0; i < locations.Length; i++)
                {
                    if (FmIsZip)
                    {
                        zipBuf = sr.ReadChars(zipOffsets[i]);
                    }
                    else
                    {
                        sr.BaseStream.Position = locations[i];
                        dirBuf = sr.ReadChars(locationBytesToRead);
                    }

                    // We avoid string.Concat() in favor of directly searching char arrays, as that's WAY faster
                    if ((FmIsZip ? zipBuf : dirBuf).Contains(MisFileStrings.SkyObjVar))
                    {
                        // Zip reading is going to check the NewDark locations the other way round, but
                        // fortunately they're interchangeable in meaning so we don't have to do anything
                        if (locations[i] == newDarkLocation1 || locations[i] == newDarkLocation2)
                        {
                            ret.NewDarkRequired = true;
                            foundAtNewDarkLocation = true;
                            break;
                        }
                        else if (locations[i] == oldDarkThief2Location)
                        {
                            foundAtOldDarkThief2Location = true;
                            break;
                        }
                    }
                }

                if (!foundAtNewDarkLocation) ret.NewDarkRequired = false;
            }

            #endregion

            if (foundAtOldDarkThief2Location)
            {
                return (ScanOptions.ScanNewDarkRequired ? (bool?)false : null,
                        ScanOptions.ScanGameType ? Games.TMA : null);
            }

            if (!ScanOptions.ScanGameType) return (ret.NewDarkRequired, (string)null);

            #region Check for T2-unique value in .gam or .mis (determines game type for both OldDark and NewDark)

            if (FmIsZip)
            {
                // For zips, since we can't seek within the stream, the fastest way to find our string is just to
                // brute-force straight through.
                using (var zipEntryStream = gamFileExists ? gamFileZipEntry.Open() : misFileZipEntry.Open())
                {
                    var identString = gamFileExists
                        ? MisFileStrings.Thief2UniqueStringGam
                        : MisFileStrings.Thief2UniqueStringMis;

                    // To catch matches on a boundary between chunks, leave extra space at the start of each
                    // chunk for the last boundaryLen bytes of the previous chunk to go into, thus achieving a
                    // kind of quick-n-dirty "step back and re-read" type thing. Dunno man, it works.
                    var boundaryLen = identString.Length;
                    const int bufSize = 81_920;
                    var chunk = new byte[boundaryLen + bufSize];

                    while (zipEntryStream.Read(chunk, boundaryLen, bufSize) != 0)
                    {
                        if (chunk.Contains(identString))
                        {
                            ret.Game = Games.TMA;
                            break;
                        }

                        // Copy the last boundaryLen bytes from chunk and put them at the beginning
                        for (int si = 0, ei = bufSize; si < boundaryLen; si++, ei++) chunk[si] = chunk[ei];
                    }

                    if (string.IsNullOrEmpty(ret.Game)) ret.Game = Games.TDP;
                }
            }
            else
            {
                // For uncompressed files on disk, we mercifully can just look at the TOC and then seek to the
                // OBJ_MAP chunk and search it for the string. Phew.
                using (var br = new BinaryReader(File.Open(misFileOnDisk, FileMode.Open, FileAccess.Read),
                    Encoding.ASCII, leaveOpen: false))
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

                        var content = br.ReadBytes((int)length);
                        ret.Game = content.Contains(MisFileStrings.Thief2UniqueStringMis)
                            ? Games.TMA
                            : Games.TDP;
                        break;
                    }
                }
            }

            #endregion

            return ret;
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
