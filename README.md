# FMScanner

_NOTE: This code is released under the Unlicense except portions which are otherwise specified. The SimpleHelpers.FileEncoding code is released under the MIT license (because it's not mine)._

A fast, thorough, accurate scanner for Thief 1 and 2 fan missions.

Detects the following:
- Title (along with a list of alternate titles if more than one is detected and they don't all match)
- Titles of campaign missions if it's a campaign
- Author
- Description (if specified in fm.ini)
- Game (Thief 1 or Thief 2)
- Languages (work in progress)
- Version (if specified; work in progress)
- Whether the mission requires NewDark
- The minimum required NewDark version (if specified; work in progress)
- Last updated date (only if specified in fm.ini currently)
- Whether the mission has any of the following:
  - Map
  - Automap
  - Custom textures
  - Custom objects
  - Custom AIs (creatures, guards, etc.)
  - Custom sounds
  - Custom movies
  - Custom motions
  - Custom scripts
  - Custom subtitles (NewDark-style only)

## Usage

```csharp
// Set these to what you want. The fewer things that are scanned for, the faster the scan will be.
// This is optional. If you call Scan() without providing a ScanOptions object, all options will default to true.
var scanOptions = new FMScanner.ScanOptions
{
    ScanTitle = true,
    ScanCampaignMissionNames = true,
    ScanAuthor = true,
    ScanVersion = true,
    ScanLanguages = true,
    ScanGameTypeAndNewDark = true,
    ScanNewDarkMinimumVersion = true,
    ScanCustomResources = true
};

// In most cases archives will be scanned without requiring an extract to disk, but when that's not the case,
// they will be temporarily extracted to this directory.
var tempPath = "C:\\MyTempDir\\FmScanTemp";


// --- Scan a single FM:

// This can be either an archive (.zip, .7z) or a directory. The scanner detects based on extension.
var fm = "C:\\FMs\\Rocksbourg3.zip";

FMScanner.ScannedFMData fmData;
using (var scanner = new FMScanner.Scanner())
{
    fmData = scanner.Scan(fm, tempPath, scanOptions);
}

// do something with fmData here


// --- Scan multiple FMs:

// The list can contain both archives (.zip, .7z) and directories. The scanner detects based on extension.
var fms = new List<string>
{
    "C:\\FMs\\BrokenTriad.zip",
    "C:\\FMs\\Racket.7z",
    "C:\\Thief2\\FMs\\SevenSisters_The"
};

List<FMScanner.ScannedFMData> fmDataList;
using (var scanner = new FMScanner.Scanner())
{
    fmDataList = scanner.Scan(fms, tempPath, scanOptions);
}

// do something with fmDataList here
```
