# YouTube MP3 for Windows

Save a YouTube video's audio as an MP3. Use a single link, paste a list, or load a Notepad text file with one video URL per line. Use the app to save audio you own or have permission to download.

## Run the app

1. Extract the Windows ZIP and open `YouTube-MP3.exe`.
2. On first run, select **Setup tools** and follow the WinGet prompts in the separate window. Close that window when setup finishes.
3. Paste links or select **Load .txt**. Use **Browse…** to choose a folder. The default is `Music\YouTube MP3`.
4. Select **Save MP3**. Each row shows the result of one link. Select **Open folder** to find the saved files.

Watch, `youtu.be`, Shorts, YouTube Music, and individual live-video links are accepted. The app saves one video per URL, ignores blank lines, and handles each video ID once per batch. If a link fails, the batch continues. If a filename already exists, the next copy gets a numbered suffix.

## Build from source

Run this in PowerShell on Windows 10 or 11 with .NET Framework 4.8 or later.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build creates `dist\YouTube-MP3-Windows.zip`. It uses the Windows .NET Framework compiler and has no NuGet dependencies.

## Audio utilities

Setup uses Windows Package Manager to install [yt-dlp](https://github.com/yt-dlp/yt-dlp), [FFmpeg](https://ffmpeg.org/), and [Deno](https://deno.com/). The app also looks for those executables in its own `tools` folder, alongside the EXE, and on `PATH`. It can use Node.js 22 or newer when Deno is unavailable. Keep `ffmpeg.exe` and `ffprobe.exe` together if you install FFmpeg manually.

If YouTube changes break downloads, update the utilities from the extracted app folder and retry.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\setup-tools.ps1 -Update
```

The app uses yt-dlp's audio download and FFmpeg conversion. Private, unavailable, and currently streaming videos may fail or take longer to finish. The Results column shows the downloader's error when available.
