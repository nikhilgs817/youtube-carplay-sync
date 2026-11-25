# Install, update and recover

YouTube CarPlay Sync 1.4.0 for Windows 10 and 11.

## What you need

| Need | Why | Where |
|---|---|---|
| Windows 10 or 11 | The app is a Windows desktop app. | |
| .NET Framework 4.8 | Builds and runs the app. Built into Windows 10 (1903 and later) and 11. | Settings › Apps › Optional features, or Microsoft's download page |
| Apple Music for Windows | Receives the songs. Open it once so it makes its Media folder. | Microsoft Store |
| Apple Devices for Windows | Syncs the songs to your iPhone. | Microsoft Store |
| About 300 MB free | The audio tools (about 150 MB) and your songs. | |
| Optional: an AI | Better language and genre. Ollama on this PC, or an API key for Claude, ChatGPT, Gemini or Grok. | See the README's *AI for song details* |

No administrator rights are needed: everything installs for your Windows account only.

## Install

### From the source folder (this ZIP or the GitHub repo)

1. Extract the ZIP: right-click it › **Extract All** › open the extracted folder.
2. Double-click **Build.cmd**. It compiles the app with the compiler that ships with Windows and opens the new installer. If Windows SmartScreen asks, select **More info › Run anyway** (the installer you just built isn't signed).
3. In the installer select **Install app**. Setup downloads yt-dlp, FFmpeg and Deno with a progress bar, adds Start menu and desktop shortcuts, and registers the app in **Settings › Apps › Installed apps**.
4. Open **YouTube CarPlay Sync**.

### With Claude Cowork

Open Claude Cowork on the PC, attach this folder (or the ZIP), and paste the prompt from [COWORK-INSTALL-PROMPT.md](../COWORK-INSTALL-PROMPT.md). Claude checks the requirements, builds, installs and confirms the app opens, and asks you before anything that needs your yes.

### First run

1. **Tools ready** shows at the top right. If it says **Setup needed**, select **Install tools**.
2. **Settings › Apple Music › Default playlist**: type a name, for example `CarPlay`.
3. **In Apple Music:** **+** next to Playlists › **New Smart Playlist** › rule **Comments is CarPlay**, tick **Live updating**, **OK**, name it `CarPlay`.
4. **Settings › AI for song details**: pick how to connect AI, add the key or server, pick a model and select **Test & load models**. Or leave **No AI**.
5. Paste a YouTube link and select **Add songs**.

## Update

Build or download the newer version and run its installer. It replaces the app and keeps:

- your settings, default playlist and encrypted API keys (`%LOCALAPPDATA%\YouTubeMP3\models.json`);
- songs waiting for review (`songs.json`);
- your audio tools and downloaded music.

Settings from 1.3 carry over: Ollama or vLLM stays selected, and AI switched off becomes **No AI**.

## Uninstall

**Settings › Apps › Installed apps › YouTube CarPlay Sync › Uninstall.** This removes the app, its shortcuts and the downloaded tools. Your music in Apple Music and in `Music\YouTube MP3` stays.

## If installation does not finish

| What happens | What to do |
|---|---|
| **Build.cmd says "The .NET Framework compiler was not found"** | Turn on .NET Framework 4.8 (Control Panel › Programs › Turn Windows features on or off), then run Build.cmd again. |
| **Build.cmd closes at once, or PowerShell scripts are blocked** | Open PowerShell in the folder and run `powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1`. On a managed PC, your IT policy may block this. |
| **SmartScreen blocks the installer** | **More info › Run anyway**. On a managed PC, ask IT to allow it. |
| **Tools download fails during setup** | The app installs anyway. Open it and select **Install tools** when you're online. |
| **Antivirus removes yt-dlp.exe** | Restore it from quarantine and allow `%LOCALAPPDATA%\YouTubeMP3\tools`, then **Settings › Audio tools › Repair tools**. |
| **Songs don't reach Apple Music** | The main screen says why. See *When songs don't reach Apple Music* in the README, or press **F1** in the app. |
