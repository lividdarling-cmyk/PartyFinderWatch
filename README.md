# Party Finder Watch

A Dalamud plugin that watches the Party Finder listings received by your FFXIV client. The plugin follows your current data center and does not use an external listing database.

## Local test build

1. Build with `dotnet build -c Release` using the .NET 10 SDK. The build uses Dalamud API 15 development libraries installed by XIVLauncher. Set `DALAMUD_HOME` if they are installed in a different location.
2. In FFXIV, open `/xlsettings` → **Experimental** → **Dev Plugin Locations** and add the full path to `bin/Release/net10.0-windows/PartyFinderWatch.dll`. The plugin manifest stays beside the DLL.
3. Open `/xlplugins` → **Dev Tools** → **Installed Dev Plugins** and enable **Party Finder Watch**. With native Party Finder closed, use `/pfwatch` to open the custom window.
4. Click **Refresh now**. Check for a new timestamp, listings in **All listings**, and **Background refresh** on the **Last data** line. Verify this once after logging in before opening native Party Finder.
5. Close the custom window to the **PF** icon. With **Automatic refresh** enabled, wait one full interval and reopen the icon to verify that **Last data** advanced. For an alert test, use a watch for a category with an existing listing and leave its keyword list blank. Refreshing the same listing again should stay quiet.

Automatic refresh pauses during duties and area transitions. If you turn off **Refresh with native Party Finder closed**, open the native window on **Data Center → All** to refresh. **All listings** shows listings received by the client, including any additional pages you browse manually. Listings that end early may remain visible until their listed timer expires.

## Watches

Use **Settings** to save independent watches for all Party Finder listings, a category, or selected duties. Add optional keywords or phrases with **+** or paste comma-separated terms. A watch matches if at least one term matches the listing description. Matching ignores letter case and surrounding punctuation. A watch without terms matches all listings in its selected scope.

Use the sound dropdown in **Settings** to select one of FFXIV's built-in chat sounds (`<se.1>`–`<se.16>`). **Preview sound** plays the selected sound locally. Turn off **Play a sound for new matches** to mute automatic alerts.
