# Party Finder Watch

A Dalamud plugin that watches the Party Finder listings received by your FFXIV client. The plugin follows your current data center and does not use an external listing database.

## Local test build

1. Build with `dotnet build -c Release` using the .NET 10 SDK. The build uses Dalamud API 15 development libraries installed by XIVLauncher. Set `DALAMUD_HOME` if they are installed in a different location.
2. In FFXIV, open `/xlsettings` → **Experimental** → **Dev Plugin Locations** and add the full path to `bin/Release/net10.0-windows/PartyFinderWatch.dll`. The plugin manifest stays beside the DLL.
3. Open `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** and enable **Party Finder Watch**. Open native Party Finder on **Data Center → All** and use `/pfwatch` to open the custom window.
4. Check **All listings** and the listing and batch counters. Compare the native first page and a later page with what the plugin receives. **Refresh now** asks native Party Finder for an update. Navigate to additional pages manually in this test build.
5. For an alert test, add a watch for a category with an existing listing and leave its keyword list blank. Close the custom window, drag the **PF** button somewhere convenient, and refresh. Open the icon to acknowledge the alert, then refresh again and check that the same listing stays quiet.

In this build, leave native Party Finder open. **All listings** contains the listings the client has received and may temporarily include listings that ended early. Seeing new results with the native window closed and automatic coverage across pages are separate in-game checks.

## Watches

Use **Settings** to save independent watches for all Party Finder listings, a category, or selected duties. Add optional keywords or phrases with **+** or paste comma-separated terms. A watch matches if at least one term matches the listing description. Matching ignores letter case and surrounding punctuation. A watch without terms matches all listings in its selected scope.
