# YouTube CarPlay Sync

Find songs, build a tagged Windows music library, and get your tracks into Apple Music, your iPhone and CarPlay. Use the downloader only for audio you own or have permission to download. This project is not affiliated with YouTube or Apple.

## Install on Windows

Open `YouTube-CarPlay-Sync-Setup.exe` and select **Install app**. Setup:

- installs the app for your Windows account and adds Start menu and desktop shortcuts;
- downloads the audio tools it needs (yt-dlp, FFmpeg and Deno, about 150 MB) into the app's own folder, so there is no separate tools step;
- registers the app in **Settings › Apps › Installed apps**.

Run a newer installer to update. Updating and uninstalling keep your music, waiting songs and settings. Uninstall removes the downloaded tools.

If the tool download fails during setup (for example while offline), the app installs anyway and shows **Install tools** on its first screen.

## How it works: one screen

Paste one or more YouTube links (one per line) and select **Add songs**, or type a song name and press Enter, then pick the right match. **Load .txt** reads a file with one link per line, and Ctrl+V pastes anywhere in the window.

Every song then moves through five steps on its own, shown live on its card:

| Step | What happens |
|---|---|
| Download | yt-dlp fetches the audio. A ring around the step shows the percentage. |
| Convert | FFmpeg converts it to MP3 at the quality set in Settings. |
| Cover art | Square cover art is embedded. |
| Song info | Title, artist, album or film, year, track and official cover art from Apple's catalogue (MusicBrainz as fallback); your local model picks the match and sets language and genre. |
| Apple Music | Waits for you, then imports the song. |

The running step pulses red and the vinyl disc spins. Finished steps fill in.

**Your turn.** When songs finish the first four steps they wait at **Your turn**. Select **Review & send**, check title, artist, album, year, language and genre for each song in one table (the last column says where the info came from), and select **Send**. Fix tags here: Apple Music reads them only when it imports a song. Untick a song to keep it waiting. Waiting songs are saved and survive closing the app or a restart.

**Into Apple Music.** The app writes the tags (the Genre tag becomes `Language - Genre`, for example `Hindi - Romantic`) and copies the MP3 into Apple Music's *Automatically Add to Apple Music* folder. It then confirms that Apple Music took the song (filed under *Artist › Album*) and removes the app's own copy. If Apple Music rejects a file, the row says so. To fix a song already in Apple Music, right-click it › **Look up song info again (and re-send)**, then delete the old copy in Apple Music. Apple Music must be running to finish an import.

**Playlists.** When a song starts a new group, a banner says so. Create one Smart Playlist for it in Apple Music (+ next to Playlists › New Smart Playlist › *Genre is Hindi - Romantic*, with Live updating on). Later songs in that group join it by themselves.

Failed songs say why and offer **Retry**; right-click a song for more actions. A link from a mix or playlist (`&list=…`) downloads only that one video, and songs already in Apple Music are not sent twice. When YouTube changes something, the app updates yt-dlp and retries automatically.

## iPhone and CarPlay

1. **Sync your iPhone** in Apple Devices › Music. Use a cable the first time, then turn on **Show this iPhone when on Wi-Fi** to sync wirelessly.
2. **Play in CarPlay** with Siri: "Hey Siri, play my Hindi Romantic playlist."

No Apple Music subscription is required.

[Smart Playlists](https://support.apple.com/guide/music-windows/mus1712973f4/windows) · [Apple Devices music sync](https://support.apple.com/guide/devices-windows/mchlbf6a1fab/windows)

## Settings (gear, top right)

- **Audio tools**: version and status of yt-dlp, FFmpeg and Deno, with **Install tools** / **Repair tools**, **Update yt-dlp** and **Open tools folder**. The tools live in `%LOCALAPPDATA%\YouTubeMP3\tools`.
- **Downloads**: the save folder (default `Music\YouTube MP3`), MP3 quality, cover art, and **Use YouTube sign-in from Firefox** for videos where YouTube asks you to sign in.
- **Local AI**: **Ollama** (`http://127.0.0.1:11434`) or **vLLM** (`http://127.0.0.1:8000/v1`); select **Test & load models**. Only titles and existing tags are sent to your server. Good models for Indian-language titles: `qwen3:30b-a3b`, `gemma3:27b`. **Ollama web search key** (optional, free from ollama.com › Settings › Keys) adds a web search when the music catalogues find nothing; the song's title and channel are sent to ollama.com.
- **Apple Music**: the *Automatically Add to Apple Music* folder. It's found automatically (usually `Music\Apple Music\Media\Automatically Add to Apple Music`); set it here only if yours is elsewhere. **Remove the app's copy once Apple Music has the song** is on by default.

## Build from source

On Windows 10 or 11 with .NET Framework 4.8, double-click `Build.cmd`, or run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build creates `dist\YouTube-CarPlay-Sync-Setup.exe` and `dist\YouTube-CarPlay-Sync-Windows.zip` with the Windows .NET Framework compiler. There are no NuGet dependencies.

## Where things are stored

| What | Where |
|---|---|
| App | `%LOCALAPPDATA%\Programs\YouTube MP3\YouTube-MP3.exe` |
| Audio tools | `%LOCALAPPDATA%\YouTubeMP3\tools` |
| Settings | `%LOCALAPPDATA%\YouTubeMP3\models.json` |
| Library tags | `%LOCALAPPDATA%\YouTubeMP3\Catalogs` (copied once from the 1.2 location) |
| Downloads | `Music\YouTube MP3` unless you choose another folder |
