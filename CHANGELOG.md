# Changelog

## 1.3.0

### Changed
- **One screen, one flow.** The Add music, Library and CarPlay sync tabs are replaced by a single screen. Paste links (or search by name) and each song moves through Download, Convert, Cover art and AI tags on its own, with a live animated step track on its card. Songs then wait at **Your turn**: **Review & send** checks every waiting song's language and genre in one table and sends them to Apple Music. Waiting songs are saved and survive a restart. Settings moved behind the gear.
- **Straight into Apple Music.** Sent songs are tagged and copied into Apple Music's *Automatically Add to Apple Music* folder, and the app confirms each import (or reports a file Apple Music rejected). A banner names each new genre group so you can make its Smart Playlist once.
- **Neumorphic look** on a 60/30/10 palette: cloud grey `#E8ECF2`, slate `#3D4B63`, red `#FF3D57` for the running step and the one action on screen. The installer and Settings share it.
- Only one copy of the app runs at a time; opening it again brings the open window forward.
- **Full song info from music catalogues.** Title, artist, album or film, album artist, year, track number and 1000×1000 official cover art now come from Apple's catalogue (iTunes Search, US then India store), with MusicBrainz and Cover Art Archive as a fallback. YouTube's own track/artist/album fields and the cleaned video title are the search terms, and the local AI only picks the right match and sets language and genre. Apple Music files songs under *Artist › Album* instead of *Unknown Album*. The review table shows every field and where it came from.
- **The app's copy is removed once Apple Music has the song** (Settings › Apple Music, on by default), so each song lives in one place.
- Right-click › **Look up song info again (and re-send)** fixes a song already in Apple Music; a banner reminds you to delete the old copy there.

### Fixed
- Songs re-sent from Apple Music showed the language twice ("Hindi - Hindi - Romantic").
- Junk `description`, `synopsis` and `purl` tags left by yt-dlp are removed before import.
- The review and settings windows now fit on a portrait (narrow) monitor.
- **Downloaded songs were all grouped as "Music".** yt-dlp writes YouTube's video category into the genre tag. The library now treats YouTube category names as a blank genre, so you or the local AI can set a real one.
- The Windows .NET Framework compiler (C# 5) rejected `await` inside `catch` and `finally` blocks in the download retry, Ollama retry and tool refresh code.
- **MP3 conversion failed for Hindi and other non-English titles.** yt-dlp printed the saved path in the Windows console's code page, which dropped or garbled those characters, so the app reported "The converter did not produce an MP3 file" even though the MP3 existed. yt-dlp now writes UTF-8, and the app finds the MP3 in the job's own work folder instead of trusting console output.
- **Every download failed with an older yt-dlp.** The app passes options (`--js-runtimes`, `--no-plugin-dirs`) that older yt-dlp builds from WinGet or `PATH` reject. The app now installs and prefers its own up-to-date copy.
- Progress stayed on "Connecting…" and some errors showed a progress line instead of the real message.
- Downloads from GitHub now use TLS 1.2. The compiler-built EXE previously defaulted to older protocols.

### Added
- Optional **Ollama web search** as an extra song-info source when the catalogues have no confirmed match, including when the local AI turns down the closest catalogue result (Settings › Local AI; needs an Ollama account key, saved as soon as it's pasted and stored encrypted for your Windows account). If Ollama refuses the search (bad key, no searches left on the account, rate limit), the song's status says why.
- Setup downloads yt-dlp, FFmpeg and Deno during installation, with progress. The separate WinGet "Setup tools" window is gone.
- A tools manager in Settings with versions, repair and update. yt-dlp updates itself about once a day, and once more automatically when YouTube changes break a download.
- A download queue: per-song progress and stage, Retry failed, Clear finished, right-click actions, and taskbar progress.
- A single input that accepts a song name or links, with a match list so an unrelated song is never chosen silently.
- Already-downloaded videos are skipped instead of being converted again.
- MP3 quality choice (Best, 320k, 192k, 128k) and square cover art embedded from the video thumbnail.
- Plain-language error messages with a suggested fix: private, age-restricted, members-only, live, unavailable, bot check, network and outdated-tool cases.
- Optional Firefox sign-in cookies for videos that need a signed-in session.
- Desktop shortcut from setup.
- Settings page for local AI: provider, address, model test, timeout, and an on/off switch. AI is optional, and its suggestions are never saved without review.
- CarPlay sync page shows honest step status: the app marks only the export as done, and Wi-Fi sync and Siri tips are included.

### Changed
- A new look modelled on the Phoenix design, in YouTube red: a near-black canvas with red for the selected tab and the primary action, a top bar with tabs, count tiles, rounded cards, status pills that pair a word with a shape, a dark title bar and dark scrollbars. Work in progress and AI suggestions are shown in blue so red never reads as an error.
- Library tags moved from the old prototype folder to `%LOCALAPPDATA%\YouTubeMP3\Catalogs`. Existing tags are copied there on first run.
- Ollama requests turn off "thinking" so reasoning models such as Qwen3 answer quickly.
- Uninstall removes the downloaded tools but keeps music, tags and settings.
