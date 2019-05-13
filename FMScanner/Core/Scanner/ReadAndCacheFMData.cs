/*
FMScanner - A fast, thorough, accurate scanner for Thief 1 and Thief 2 fan missions.

Written in 2017-2019 by FenPhoenix.

To the extent possible under law, the author(s) have dedicated all copyright and related and neighboring rights
to this software to the public domain worldwide. This software is distributed without any warranty.

You should have received a copy of the CC0 Public Domain Dedication along with this software.
If not, see <http://creativecommons.org/publicdomain/zero/1.0/>.
*/

using System.Collections.Generic;
using System.IO;
using System.Linq;
using static System.IO.Path;
using static FMScanner.FMConstants;
using static FMScanner.Methods;

namespace FMScanner
{
    public partial class Scanner
    {
        private (bool NoBaseDirFiles, bool Thief3Found, bool BasePlusOneFound)
        IterateFiles(
            List<NameAndIndex> allMisFiles,
            List<NameAndIndex> baseDirFiles,
            List<NameAndIndex> stringsDirFiles,
            List<NameAndIndex> intrfaceDirFiles, List<NameAndIndex> booksDirFiles,
            List<NameAndIndex> t3FMExtrasDirFiles, int archiveEntriesCount, ScannedFMData fmd,
            bool basePlusOneIsBase = false, string newBaseDir = null)
        {
            bool t3Found = false;
            string fnThisIter;

            bool FNStartsWithI_TI(string substr) => fnThisIter.StartsWithI(substr);
            bool FNEndsWithI_TI(string substr) => fnThisIter.EndsWithI(substr);
            bool FNContainsI_TI(char c) => fnThisIter.Contains(c);
            void Add_TI(List<NameAndIndex> list, int index)
            {
                list.Add(new NameAndIndex { Name = fnThisIter, Index = index });
            }

            if (FmIsZip || ScanOptions.ScanSize)
            {
                // TODO:
                // SS2: This list iteration actually takes < 2.45% of the time of this method. So we can easily
                // afford to do a double-iteration in the extremely rare case of needing to set base+1 as base.
                // That way, we can just bump everything down by one dir in the second iteration and we're good.
                // TODO:
                // If doing a second iteration, don't look for Thief 3. Any other non-SS2 thing can be skipped as
                // well. We don't need to be encouraging or supporting sloppy archive directory structures here.
                // TODO:
                // If on second iteration, still store original-base files for pulling readmes from them
                for (var i = 0; i < (FmIsZip ? archiveEntriesCount : FmDirFiles.Count); i++)
                {
                    var fn = FmIsZip
                        ? Archive.Entries[i].FullName
                        : FmDirFiles[i].FullName.Substring(FmWorkingPath.Length);

                    fnThisIter = basePlusOneIsBase ? fn.Substring(newBaseDir.Length) : fn;

                    var index = FmIsZip ? i : -1;

                    // Don't need to do this on second iteration
                    // But needs to be done separately because we don't want to exclude .mis files from the below
                    // checks
                    if (!basePlusOneIsBase && fn.EndsWithI(".mis"))
                    {
                        allMisFiles.Add(new NameAndIndex { Name = fn, Index = index });
                    }

                    if (!basePlusOneIsBase &&
                        !t3Found &&
                        fn.StartsWithI(FMDirs.T3DetectS(dsc)) &&
                        fn.CountChars(dsc) == 3 &&
                        fn.EndsWithI(".ibt") ||
                        fn.EndsWithI(".cbt") ||
                        fn.EndsWithI(".gmp") ||
                        fn.EndsWithI(".ned") ||
                        fn.EndsWithI(".unr"))
                    {
                        fmd.Game = Games.TDS;
                        t3Found = true;
                        continue;
                    }
                    // We can't early-out if !t3Found here because if we find it after this point, we'll be
                    // missing however many of these we skipped before we detected Thief 3
                    else if (!basePlusOneIsBase &&
                             fn.StartsWithI(FMDirs.T3FMExtras1S(dsc)) ||
                             fn.StartsWithI(FMDirs.T3FMExtras2S(dsc)))
                    {
                        t3FMExtrasDirFiles.Add(new NameAndIndex { Name = fn, Index = index });
                        continue;
                    }
                    else if (!FNContainsI_TI(dsc) && FNContainsI_TI('.'))
                    {
                        Add_TI(baseDirFiles, index);
                        // Fallthrough so ScanCustomResources can use it
                    }
                    else if (!t3Found && FNStartsWithI_TI(FMDirs.StringsS(dsc)))
                    {
                        Add_TI(stringsDirFiles, index);
                        continue;
                    }
                    else if (!t3Found && FNStartsWithI_TI(FMDirs.IntrfaceS(dsc)))
                    {
                        Add_TI(intrfaceDirFiles, index);
                        // Fallthrough so ScanCustomResources can use it
                    }
                    else if (!t3Found && FNStartsWithI_TI(FMDirs.BooksS(dsc)))
                    {
                        Add_TI(booksDirFiles, index);
                        continue;
                    }

                    // Do this at the same time for performance. We cut the time roughly in half by doing this.
                    if (!t3Found && ScanOptions.ScanCustomResources) CheckForCustomResources(fnThisIter, fmd);
                }

                // Thief 3 can have an empty base dir, and we don't scan for custom resources for T3
                if (!t3Found)
                {
                    if (baseDirFiles.Count == 0) return (true, false, false);

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
            }
            else
            {
                // TODO: Add base+1 functionality to this code path too (no-size-scan&&folder code path)
                // Here, we can much more quickly and easily detect which dir .mis files are in. Search base dir,
                // if none, enumerate dirs in base dir, search each top, first one we find is new base, if none
                // then reject
                var t3DetectPath = Path.Combine(FmWorkingPath, FMDirs.T3Detect);
                if (Directory.Exists(t3DetectPath) &&
                    FastIO.FilesExistSearchTop(t3DetectPath, "*.ibt", "*.cbt", "*.gmp", "*.ned", "*.unr"))
                {
                    t3Found = true;
                    fmd.Game = Games.TDS;
                }

                foreach (var f in EnumFiles("*", SearchOption.TopDirectoryOnly))
                {
                    baseDirFiles.Add(new NameAndIndex { Name = GetFileName(f) });
                }

                foreach (var d in Directory.EnumerateDirectories(FmWorkingPath, "*", SearchOption.TopDirectoryOnly))
                {
                    var misFiles = Directory.EnumerateFiles(d, "*.mis", SearchOption.TopDirectoryOnly);
                    foreach (var f in misFiles)
                    {
                        allMisFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }
                }

                if (t3Found)
                {
                    foreach (var f in EnumFiles(FMDirs.T3FMExtras1, "*", SearchOption.TopDirectoryOnly))
                    {
                        t3FMExtrasDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }

                    foreach (var f in EnumFiles(FMDirs.T3FMExtras2, "*", SearchOption.TopDirectoryOnly))
                    {
                        t3FMExtrasDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }
                }
                else
                {
                    if (baseDirFiles.Count == 0) return (true, false, false);

                    foreach (var f in EnumFiles(FMDirs.Strings, "*", SearchOption.AllDirectories))
                    {
                        stringsDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }

                    foreach (var f in EnumFiles(FMDirs.Intrface, "*", SearchOption.AllDirectories))
                    {
                        intrfaceDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }

                    foreach (var f in EnumFiles(FMDirs.Books, "*", SearchOption.AllDirectories))
                    {
                        booksDirFiles.Add(new NameAndIndex { Name = f.Substring(FmWorkingPath.Length) });
                    }

                    // TODO: Maybe extract this again, but then I have to extract MapFileExists() too
                    if (ScanOptions.ScanCustomResources)
                    {
                        var baseDirFolders = (
                            from f in Directory.EnumerateDirectories(FmWorkingPath, "*",
                                SearchOption.TopDirectoryOnly)
                            select f.Substring(f.LastIndexOf(dsc) + 1)).ToArray();

                        foreach (var f in intrfaceDirFiles)
                        {
                            if (fmd.HasAutomap == null &&
                                f.Name.StartsWithI(FMDirs.IntrfaceS(dsc)) &&
                                f.Name.CountChars(dsc) >= 2 &&
                                f.Name.EndsWithI("ra.bin"))
                            {
                                fmd.HasAutomap = true;
                                // Definitely a clever deduction, definitely not a sneaky hack for GatB-T2
                                fmd.HasMap = true;
                                break;
                            }

                            if (fmd.HasMap == null && MapFileExists(f.Name)) fmd.HasMap = true;
                        }

                        if (fmd.HasMap == null) fmd.HasMap = false;
                        if (fmd.HasAutomap == null) fmd.HasAutomap = false;

                        fmd.HasCustomMotions =
                            baseDirFolders.ContainsI(FMDirs.Motions) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Motions),
                                MotionFilePatterns);

                        fmd.HasMovies =
                            baseDirFolders.ContainsI(FMDirs.Movies) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Movies), "*");

                        fmd.HasCustomTextures =
                            baseDirFolders.ContainsI(FMDirs.Fam) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Fam),
                                ImageFilePatterns);

                        fmd.HasCustomObjects =
                            baseDirFolders.ContainsI(FMDirs.Obj) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Obj), "*.bin");

                        fmd.HasCustomCreatures =
                            baseDirFolders.ContainsI(FMDirs.Mesh) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Mesh), "*.bin");

                        fmd.HasCustomScripts =
                            baseDirFiles.Any(x => ScriptFileExtensions.ContainsI(GetExtension(x.Name))) ||
                            (baseDirFolders.ContainsI(FMDirs.Scripts) &&
                             FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Scripts), "*"));

                        fmd.HasCustomSounds =
                            baseDirFolders.ContainsI(FMDirs.Snd) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Snd), "*");

                        fmd.HasCustomSubtitles =
                            baseDirFolders.ContainsI(FMDirs.Subtitles) &&
                            FastIO.FilesExistSearchAll(Path.Combine(FmWorkingPath, FMDirs.Subtitles), "*.sub");
                    }
                }
            }

            return (false, t3Found, false);
        }

        // TODO: This might be hardcoded to base dir? Check this!
        private bool MapFileExists(string path)
        {
            if (path.StartsWithI(FMDirs.IntrfaceS(dsc)) &&
                path.CountChars(dsc) >= 2)
            {
                var lsi = path.LastIndexOf(dsc);
                if (path.Length > lsi + 5 &&
                    path.Substring(lsi + 1, 5).EqualsI("page0") &&
                    path.LastIndexOf('.') > lsi)
                {
                    return true;
                }
            }

            return false;
        }

        private void CheckForCustomResources(string fn, ScannedFMData fmd)
        {
            if (fmd.HasAutomap == null &&
                fn.StartsWithI(FMDirs.IntrfaceS(dsc)) &&
                fn.CountChars(dsc) >= 2 &&
                fn.EndsWithI("ra.bin"))
            {
                fmd.HasAutomap = true;
                // Definitely a clever deduction, definitely not a sneaky hack for GatB-T2
                fmd.HasMap = true;
            }
            else if (fmd.HasMap == null && MapFileExists(fn))
            {
                fmd.HasMap = true;
            }
            else if (fmd.HasCustomMotions == null &&
                     fn.StartsWithI(FMDirs.MotionsS(dsc)) &&
                     MotionFileExtensions.Any(fn.EndsWithI))
            {
                fmd.HasCustomMotions = true;
            }
            else if (fmd.HasMovies == null &&
                     (fn.StartsWithI(FMDirs.MoviesS(dsc)) || fn.StartsWithI(FMDirs.CutscenesS(dsc))) &&
                     fn.HasFileExtension())
            {
                fmd.HasMovies = true;
            }
            else if (fmd.HasCustomTextures == null &&
                     fn.StartsWithI(FMDirs.FamS(dsc)) &&
                     ImageFileExtensions.Any(fn.EndsWithI))
            {
                fmd.HasCustomTextures = true;
            }
            else if (fmd.HasCustomObjects == null &&
                     fn.StartsWithI(FMDirs.ObjS(dsc)) &&
                     fn.EndsWithI(".bin"))
            {
                fmd.HasCustomObjects = true;
            }
            else if (fmd.HasCustomCreatures == null &&
                     fn.StartsWithI(FMDirs.MeshS(dsc)) &&
                     fn.EndsWithI(".bin"))
            {
                fmd.HasCustomCreatures = true;
            }
            else if (fmd.HasCustomScripts == null &&
                     (!fn.Contains(dsc) &&
                      ScriptFileExtensions.Any(fn.EndsWithI)) ||
                     (fn.StartsWithI(FMDirs.ScriptsS(dsc)) &&
                      fn.HasFileExtension()))
            {
                fmd.HasCustomScripts = true;
            }
            else if (fmd.HasCustomSounds == null &&
                     (fn.StartsWithI(FMDirs.SndS(dsc)) || fn.StartsWithI(FMDirs.Snd2S(dsc))) &&
                     fn.HasFileExtension())
            {
                fmd.HasCustomSounds = true;
            }
            else if (fmd.HasCustomSubtitles == null &&
                     fn.StartsWithI(FMDirs.SubtitlesS(dsc)) &&
                     fn.EndsWithI(".sub"))
            {
                fmd.HasCustomSubtitles = true;
            }
        }

        private bool ReadAndCacheFMData(ScannedFMData fmd, List<NameAndIndex> baseDirFiles,
            List<NameAndIndex> misFiles, List<NameAndIndex> usedMisFiles, List<NameAndIndex> stringsDirFiles,
            List<NameAndIndex> intrfaceDirFiles, List<NameAndIndex> booksDirFiles,
            List<NameAndIndex> t3FMExtrasDirFiles)
        {
            /* TODO: SS2 game plan:
            For SS2, some archives have what should be the "base dir" as a subdir one level deep.
            Strategy for dealing with this while keeping performance up:
            -Add ALL .mis files to list, not just base dir ones
            -Add all files that are one level deep to a tempBaseDirFiles list (because one-dir-deep might end up
             being set as the base dir
            -After the fact, go through MisFiles and do the following:
             -If any base-dir (non-dir-sep-char-containing) files exist, remove all other files that do have dir
              sep chars
             -If any files with only ONE dir-sep in them exist AND no files WITHOUT dir sep char exist,
              remove all others that have more than one
             -Otherwise, reject archive (because any .mis files will be in higher dirs than base or base+1, which
              means that whatever it is, it's not a valid FM)
            -If .mis files are in base, proceed as normal
            -If .mis files are in base+1, mark base+1 as "base" (code needs to be updated to allow telling it
             which dir to consider as the base) and clear-and-copy tempBaseDirFiles to baseDirFiles
            */

            #region Iterate archive filenames and split off into lists

            var basePlusOneDirFiles = new List<NameAndIndex>();
            var allMisFiles = new List<NameAndIndex>();

            // Cache entries count for possible double-iteration
            int archiveEntriesCount = FmIsZip ? Archive.Entries.Count : 0;

            var (noBaseDirFiles, t3Found, basePlusOneFound) =
                IterateFiles(allMisFiles,
                    baseDirFiles, stringsDirFiles, intrfaceDirFiles, booksDirFiles, t3FMExtrasDirFiles,
                    archiveEntriesCount, fmd);

            // Cut it right here for Thief 3: we don't need anything else
            if (t3Found) return true;

            #endregion

            #region Add MisFiles and check for none

            for (var i = 0; i < baseDirFiles.Count; i++)
            {
                var f = baseDirFiles[i];
                if (f.Name.EndsWithI(".mis"))
                {
                    misFiles.Add(new NameAndIndex { Name = GetFileName(f.Name), Index = f.Index });
                }
            }

            if (misFiles.Count == 0)
            {
                // iterate again but with one dir down
                if (allMisFiles.Count > 0)
                {
                    // NOTE: Clear all lists first!
                    // temp, remove this when ready to use
                    return false;
                }
                else
                {
                    return false;
                }
            }

            #endregion

            #region Cache list of used .mis files

            NameAndIndex missFlag = null;

            if (stringsDirFiles.Count > 0)
            {
                // I don't remember if I need to search in this exact order, so uh... not rockin' the boat.
                missFlag =
                    stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.StringsS(dsc) + FMFiles.MissFlag))
                    ?? stringsDirFiles.FirstOrDefault(x =>
                        x.Name.EqualsI(FMDirs.StringsS(dsc) + "english" + dsc + FMFiles.MissFlag))
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
    }
}
