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
