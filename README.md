# YouTube MP3 for Windows

Download MP3 audio and organize a music collection with a model running on Ollama or a self-hosted vLLM server. Use the downloader for audio you own or have permission to download.

## Install on Windows

Download `YouTube-MP3-Setup.exe` from [Releases](https://github.com/nikhilgs817/youtube-mp3-windows/releases). Open it and select **Install app**. Setup installs for your Windows account, adds a Start menu shortcut, and registers the app in **Settings → Apps → Installed apps**. Run a newer installer to update. Uninstall keeps your music and model preferences. A portable ZIP is also available.

## Download MP3 audio

1. Open **Download MP3**. Select **Setup tools** on first use and follow the WinGet prompts.
2. Paste links or select **Load .txt** to read a Notepad file with one URL per line.
3. Choose an output folder and select **Save MP3**. The default is `Music\YouTube MP3`.

Watch, `youtu.be`, Shorts, YouTube Music, and individual live-video links are accepted. The app saves one video per URL, ignores blank lines, and handles each video ID once per batch. If a link fails, the batch continues. If a filename already exists, the next copy gets a numbered suffix.

## Organize music with a local model

1. Start your model server. In **Organize music**, select **Ollama** (`http://127.0.0.1:11434`) or **vLLM** (`http://127.0.0.1:8000/v1`). Change the address when the server runs on another machine. vLLM supports an optional session-only API key and requires a chat model that supports JSON-schema outputs.
2. Select **Load models** and choose a model. For Ollama, download a model before loading the list. Cloud-tagged Ollama models are excluded.
3. Select a source music folder or choose **Use downloads**. Choose an output folder and group by **Genre**, **Language**, or **Language + genre**.
4. Select **Preview sorting**. Review or edit the suggested labels, then select **Copy selected** to write the organized copies. Example output folders include `Hindi-Pop` and `English-Rock`. Files with the same name and identical contents are skipped.

The app recursively scans the source folder for common audio formats, including MP3, M4A, FLAC and WAV. The selected server receives the filename and available song tags. Sorting quality depends on those fields and the model. The audio stays on your PC, source files stay untouched, and the model can return `Unknown` when tags are inconclusive.

## Build from source

Run this in PowerShell on Windows 10 or 11 with .NET Framework 4.8 or later.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build creates `dist\YouTube-MP3-Setup.exe` and `dist\YouTube-MP3-Windows.zip`. It uses the Windows .NET Framework compiler and has no NuGet dependencies.

## Audio utilities

Setup uses Windows Package Manager to install [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org/), and [Deno](https://deno.com/). The app also looks for those executables in its own `tools` folder, alongside the EXE, and on `PATH`. It can use Node.js 22 or newer when Deno is unavailable. Keep `ffmpeg.exe` and `ffprobe.exe` together if you install FFmpeg manually.

If YouTube changes break downloads, update the utilities from the extracted app folder and retry.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup-tools.ps1 -Update
```

The app uses yt-dlp's audio download and FFmpeg conversion. Private, unavailable, and currently streaming videos may fail or take longer to finish. The Results column shows the downloader's error when available.
