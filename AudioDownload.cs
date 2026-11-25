using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace YouTubeMp3
{
    internal sealed class VideoLink
    {
        private static readonly Regex IdPattern = new Regex(@"\A[A-Za-z0-9_-]{11}\z");
        public readonly string Original;
        public readonly string Id;

        public VideoLink(string original)
        {
            Original = original;
            Uri uri;
            if (!Uri.TryCreate(original, UriKind.Absolute, out uri) ||
                (uri.Scheme != "https" && uri.Scheme != "http") ||
                !uri.IsDefaultPort || uri.UserInfo.Length != 0)
                return;

            string[] path = uri.AbsolutePath.Trim('/').Split('/');
            string id = null;
            switch (uri.Host.ToLowerInvariant())
            {
                case "youtu.be":
                case "www.youtu.be":
                    if (path.Length == 1) id = path[0];
                    break;
                case "youtube.com":
                case "www.youtube.com":
                case "m.youtube.com":
                case "music.youtube.com":
                    if (path.Length == 1 && path[0] == "watch")
                    {
                        foreach (string parameter in uri.Query.TrimStart('?').Split('&'))
                        {
                            if (!parameter.StartsWith("v=", StringComparison.Ordinal)) continue;
                            id = Uri.UnescapeDataString(parameter.Substring(2));
                            break;
                        }
                    }
                    if (path.Length == 2 && (path[0] == "shorts" || path[0] == "live" || path[0] == "embed"))
                        id = path[1];
                    break;
            }
            if (id != null && IdPattern.IsMatch(id)) Id = id;
        }

        public static List<VideoLink> Parse(string input)
        {
            var links = new List<VideoLink>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            using (var lines = new StringReader(input))
            {
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length == 0) continue;
                    var link = new VideoLink(line);
                    if (link.Id == null || seen.Add(link.Id)) links.Add(link);
                }
            }
            return links;
        }
    }

    internal static class AudioDownload
    {
        private const string ProgressPrefix = "MP3_PROGRESS:";
        internal const string StagePrefix = ".yc-";

        // Quality choices shown in the app, mapped to yt-dlp's --audio-quality.
        internal static readonly string[] Qualities = { "Best", "320k", "192k", "128k" };

        internal static string QualityArgument(string quality)
        {
            switch (quality)
            {
                case "320k": return "320K";
                case "192k": return "192K";
                case "128k": return "128K";
                default: return "0";
            }
        }

        // Returns the existing MP3 for this video in the folder, so the same video is not converted twice.
        internal static string Existing(VideoLink link, string destination)
        {
            if (link.Id == null || !Directory.Exists(destination)) return null;
            try
            {
                foreach (string file in Directory.EnumerateFiles(destination, "*[" + link.Id + "]*.mp3"))
                    if (new FileInfo(file).Length > 0) return file;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        // Removes work folders left behind by a crash or a forced shutdown. Only folders this app creates are touched.
        internal static void CleanStaleWork(string destination)
        {
            try
            {
                if (!Directory.Exists(destination)) return;
                foreach (string pattern in new[] { StagePrefix + "*", ".mp3-work-*" })
                    foreach (string folder in Directory.EnumerateDirectories(destination, pattern))
                        if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > TimeSpan.FromHours(6))
                            Directory.Delete(folder, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public static string Save(VideoLink link, string destination, DownloadTools tools,
                                  Action<int, string> report, CancellationToken cancel)
        {
            return Save(link, destination, tools, new DownloadOptions(), report, cancel);
        }

        public static string Save(VideoLink link, string destination, DownloadTools tools, DownloadOptions options,
                                  Action<int, string> report, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (link.Id == null) throw new DownloadException("This is not a YouTube video link.", "Use a watch, youtu.be, Shorts or YouTube Music link.", false);
            if (!tools.Ready) throw new DownloadException(tools.Missing + (tools.Missing.Contains(" and ") ? " are" : " is") + " not installed.", "Open Settings and select Install tools.", false);

            string stage = Path.Combine(destination, StagePrefix + Guid.NewGuid().ToString("N").Substring(0, 10));
            var stageInfo = Directory.CreateDirectory(stage);
            try { stageInfo.Attributes |= FileAttributes.Hidden; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                var arguments = new List<string> {
                    "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--encoding", "utf-8",
                    "--newline", "--progress", "--progress-delta", "0.4", "--color", "never",
                    "--progress-template", "download:" + ProgressPrefix + "%(progress._percent_str)s",
                    "--windows-filenames", "--socket-timeout", "20", "--retries", "5", "--fragment-retries", "5",
                    "--ffmpeg-location", Path.GetDirectoryName(tools.Encoder),
                    "-f", "bestaudio/best", "--extract-audio", "--audio-format", "mp3",
                    "--audio-quality", QualityArgument(options.Quality), "--embed-metadata", "--write-info-json",
                    "-P", stage, "-o", "%(title).120B [%(id)s].%(ext)s"
                };
                if (tools.JsRuntime != null) arguments.InsertRange(3, new[] { "--no-js-runtimes", "--js-runtimes", tools.JsRuntime });
                if (options.Artwork)
                    arguments.AddRange(new[] { "--embed-thumbnail", "--convert-thumbnails", "jpg", "--ppa",
                        "ThumbnailsConvertor+FFmpeg_o:-c:v mjpeg -qmin 1 -qscale:v 2 -vf crop=\"'if(gt(ih,iw),iw,ih)':'if(gt(iw,ih),ih,iw)'\"" });
                if (!String.IsNullOrEmpty(options.CookiesBrowser))
                    arguments.AddRange(new[] { "--cookies-from-browser", options.CookiesBrowser });
                arguments.AddRange(new[] { "--", "https://www.youtube.com/watch?v=" + link.Id });

                RunResult result;
                try { result = Run(tools.Downloader, arguments.ToArray(), report, cancel); }
                catch (DownloadException failure)
                {
                    // Artwork is a nice-to-have: keep the song when only the cover step failed.
                    if (FindMp3(stage) == null || !failure.Raw.ToLowerInvariant().Contains("thumbnail")) throw;
                    result = null;
                }
                cancel.ThrowIfCancellationRequested();
                options.Info = ReadInfo(stage);
                // The file is found on disk, never by parsing console output: the console can mangle Hindi and other
                // non-English titles, which made every such download report a failed conversion.
                string path = FindMp3(stage);
                if (path == null)
                    throw new DownloadException("The converter did not produce an MP3 file.",
                        "Open Settings and select Repair tools, then retry.", false,
                        result == null ? "" : result.LastLines);

                string fileName = Path.GetFileName(path);
                string target = Path.Combine(destination, fileName);
                for (int copy = 2; ; copy++)
                {
                    try
                    {
                        File.Move(path, target);
                        return target;
                    }
                    catch (IOException)
                    {
                        if (!File.Exists(target) || copy > 999) throw;
                        target = Path.Combine(destination, Path.GetFileNameWithoutExtension(fileName) +
                                              " (" + copy.ToString(CultureInfo.InvariantCulture) + ").mp3");
                    }
                    cancel.ThrowIfCancellationRequested();
                }
            }
            finally
            {
                try { Directory.Delete(stage, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static string FindMp3(string folder)
        {
            if (!Directory.Exists(folder)) return null;
            string best = null;
            long size = 0;
            foreach (string file in Directory.EnumerateFiles(folder, "*.mp3"))
            {
                long length = new FileInfo(file).Length;
                if (length > size) { best = file; size = length; }
            }
            return best;
        }

        internal sealed class RunResult
        {
            internal int ExitCode;
            internal string LastLines = "";
        }

        private static RunResult Run(string executable, string[] arguments, Action<int, string> report,
                                     CancellationToken cancel, Action<string> output = null)
        {
            var info = new ProcessStartInfo(executable, String.Join(" ", Array.ConvertAll(arguments, Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            info.EnvironmentVariables["PYTHONUTF8"] = "1";
            var tail = new Queue<string>();
            string error = null;
            object gate = new object();
            DataReceivedEventHandler read = delegate(object sender, DataReceivedEventArgs e)
            {
                string line = e.Data;
                if (line == null) return;
                if (line.StartsWith(ProgressPrefix, StringComparison.Ordinal))
                {
                    double percent;
                    if (Double.TryParse(line.Substring(ProgressPrefix.Length).Trim().TrimEnd('%'),
                                        NumberStyles.Float, CultureInfo.InvariantCulture, out percent) && percent >= 0)
                        report(Math.Min(99, (int)percent), percent >= 100 ? "Downloaded" : "Downloading audio");
                    return;
                }
                string stage = StageOf(line);
                if (stage != null) report(-1, stage);
                if (String.IsNullOrWhiteSpace(line)) return;
                lock (gate)
                {
                    if (line.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase) && error == null) error = line.Trim();
                    tail.Enqueue(line.Trim());
                    while (tail.Count > 12) tail.Dequeue();
                }
            };
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null && output != null) output(e.Data);
                    else read(sender, e);
                };
                process.ErrorDataReceived += read;
                cancel.ThrowIfCancellationRequested();
                try { process.Start(); }
                catch (System.ComponentModel.Win32Exception ex)
                {
                    throw new DownloadException("yt-dlp could not start: " + ex.Message,
                        "Your antivirus may have blocked it. Open Settings and select Repair tools.", false);
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                while (!process.WaitForExit(200))
                {
                    if (!cancel.IsCancellationRequested) continue;
                    Stop(process);
                    break;
                }
                process.WaitForExit();
                cancel.ThrowIfCancellationRequested();
                var result = new RunResult { ExitCode = process.ExitCode };
                lock (gate) result.LastLines = String.Join(Environment.NewLine, tail.ToArray());
                if (process.ExitCode != 0)
                    throw DownloadException.Explain(error ?? LastMeaningful(tail) ??
                        "The downloader exited with code " + process.ExitCode + ".", result.LastLines);
                return result;
            }
        }

        private static string LastMeaningful(IEnumerable<string> lines)
        {
            string found = null;
            foreach (string line in lines)
                if (line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase) || line.StartsWith("WARNING", StringComparison.OrdinalIgnoreCase) ||
                    line.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0) found = line;
            return found;
        }

        private static string StageOf(string line)
        {
            if (line.StartsWith("[youtube]", StringComparison.Ordinal) || line.StartsWith("[info]", StringComparison.Ordinal))
                return "Finding the audio";
            if (line.StartsWith("[ExtractAudio]", StringComparison.Ordinal)) return "Converting to MP3";
            if (line.StartsWith("[ThumbnailsConvertor]", StringComparison.Ordinal) ||
                line.StartsWith("[EmbedThumbnail]", StringComparison.Ordinal)) return "Adding artwork";
            if (line.StartsWith("[Metadata]", StringComparison.Ordinal)) return "Writing tags";
            return null;
        }

        private static YouTubeInfo ReadInfo(string stage)
        {
            try
            {
                foreach (string file in Directory.GetFiles(stage, "*.info.json"))
                    return YouTubeInfo.Parse(File.ReadAllText(file, Encoding.UTF8));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        // YouTube's details for a video without downloading it, for songs that are already on disk.
        public static YouTubeInfo FetchInfo(VideoLink link, DownloadTools tools, CancellationToken cancel)
        {
            if (link.Id == null || tools.Downloader == null) return null;
            var response = new StringBuilder();
            var args = new List<string> { "--ignore-config", "--no-plugin-dirs", "--encoding", "utf-8", "--color", "never", "--no-playlist",
                "--skip-download", "--dump-single-json", "--socket-timeout", "15", "--retries", "2" };
            if (tools.JsRuntime != null) args.InsertRange(2, new[] { "--no-js-runtimes", "--js-runtimes", tools.JsRuntime });
            args.AddRange(new[] { "--", "https://www.youtube.com/watch?v=" + link.Id });
            Run(tools.Downloader, args.ToArray(), (percent, stage) => { }, cancel, line => response.AppendLine(line));
            return YouTubeInfo.Parse(response.ToString());
        }

        public static List<SongMatch> Search(string query, DownloadTools tools, CancellationToken cancel)
        {
            query = query.Trim();
            if (query.Length == 0) throw new ArgumentException("Enter a song name.");
            if (tools.Downloader == null) throw new DownloadException("yt-dlp is not installed.", "Open Settings and select Install tools.", false);
            var response = new StringBuilder();
            string[] args = { "--ignore-config", "--no-plugin-dirs", "--encoding", "utf-8", "--color", "never", "--flat-playlist",
                "--skip-download", "--dump-single-json", "--socket-timeout", "15", "--retries", "2",
                "--", "ytsearch8:" + query };
            Run(tools.Downloader, args, (percent, stage) => { }, cancel, line => response.AppendLine(line));
            return ParseMatches(response.ToString());
        }

        internal static List<SongMatch> ParseMatches(string response)
        {
            var matches = new List<SongMatch>();
            if (String.IsNullOrWhiteSpace(response)) return matches;
            var document = new JavaScriptSerializer().DeserializeObject(response) as IDictionary<string, object>;
            object list;
            var entries = document != null && document.TryGetValue("entries", out list) ? list as object[] : null;
            if (entries == null) return matches;
            var found = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in entries)
            {
                var entry = item as IDictionary<string, object>;
                if (entry == null) continue;
                string id = StringValue(entry, "id");
                var link = new VideoLink("https://www.youtube.com/watch?v=" + id);
                if (link.Id == null || !found.Add(link.Id)) continue;
                string title = StringValue(entry, "title");
                string artist = StringValue(entry, "channel");
                if (artist.Length == 0) artist = StringValue(entry, "uploader");
                double seconds;
                string duration = Double.TryParse(StringValue(entry, "duration"), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out seconds) && seconds >= 0 && seconds < 10000000
                    ? String.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", (int)seconds / 60, (int)seconds % 60) : "";
                matches.Add(new SongMatch { Link = link.Original, Title = title.Length == 0 ? id : title,
                    Channel = artist, Duration = duration });
            }
            return matches;
        }

        private static string StringValue(IDictionary<string, object> entry, string key)
        {
            object value;
            return entry.TryGetValue(key, out value) ? Convert.ToString(value, CultureInfo.InvariantCulture) : "";
        }

        private static void Stop(Process process)
        {
            try
            {
                if (process.HasExited) return;
                var killer = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "taskkill.exe"),
                    "/PID " + process.Id + " /T /F") { UseShellExecute = false, CreateNoWindow = true };
                using (var taskkill = Process.Start(killer)) taskkill.WaitForExit(3000);
                if (!process.HasExited) process.Kill();
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static string Quote(string value)
        {
            // CommandLineToArgvW treats backslashes before a quote specially.
            return "\"" + Regex.Replace(value, "(\\\\*)(\"|$)", match =>
                new string('\\', match.Groups[1].Length * 2) +
                (match.Groups[2].Length == 0 ? "" : "\\\"")) + "\"";
        }
    }

    internal sealed class SongMatch
    {
        internal string Link, Title, Channel, Duration;
        public override string ToString() { return Title + (String.IsNullOrEmpty(Channel) ? "" : " — " + Channel); }
    }

    internal sealed class DownloadOptions
    {
        internal string Quality = "Best";
        internal bool Artwork = true;
        internal string CookiesBrowser;
        internal YouTubeInfo Info;   // filled by Save: YouTube's own details for the video
    }

    // A failed download, in plain words, with what to do about it.
    internal sealed class DownloadException : Exception
    {
        internal readonly string Hint;
        internal readonly bool Outdated;
        internal readonly string Raw;

        internal DownloadException(string message, string hint, bool outdated, string raw = null) : base(message)
        {
            Hint = hint;
            Outdated = outdated;
            Raw = raw ?? message;
        }

        internal static DownloadException Explain(string raw, string log)
        {
            string text = (raw ?? "").Trim();
            string lower = (text + "\n" + (log ?? "")).ToLowerInvariant();
            string detail = Regex.Replace(text, @"\AERROR:\s*(\[[^\]]+\]\s*)?([A-Za-z0-9_-]{11}:\s*)?", "");
            if (lower.Contains("no such option") || lower.Contains("unrecognized arguments"))
                return new DownloadException("Your copy of yt-dlp is too old for this app.", "The app will update it and retry.", true, text);
            if (lower.Contains("confirm you") && lower.Contains("not a bot"))
                return new DownloadException("YouTube asked to confirm you're not a bot.",
                    "Wait a while and retry. Or sign in to YouTube in Firefox and turn on Firefox cookies in Settings.", false, text);
            if (lower.Contains("confirm your age") || lower.Contains("age-restricted") || lower.Contains("inappropriate for some users"))
                return new DownloadException("This video is age-restricted.",
                    "Sign in to YouTube in Firefox and turn on Firefox cookies in Settings, then retry.", false, text);
            if (lower.Contains("private video"))
                return new DownloadException("This video is private.", "Only the owner can download it.", false, text);
            if (lower.Contains("members-only") || lower.Contains("join this channel"))
                return new DownloadException("This video is for channel members only.", "It can't be downloaded without membership.", false, text);
            if (lower.Contains("live event will begin") || lower.Contains("premieres in") || lower.Contains("is live"))
                return new DownloadException("This is a live or upcoming stream.", "Retry after the stream has ended.", false, text);
            if (lower.Contains("video unavailable") || lower.Contains("has been removed") || lower.Contains("not available in your country"))
                return new DownloadException("This video is unavailable.", "It may be removed or blocked in your region.", false, text);
            if (lower.Contains("ffprobe") || lower.Contains("ffmpeg not found") || lower.Contains("postprocessing"))
                return new DownloadException("The MP3 conversion step failed.", "Open Settings and select Repair tools, then retry.", false, text);
            if (lower.Contains("getaddrinfo") || lower.Contains("timed out") || lower.Contains("connection reset") ||
                lower.Contains("network is unreachable") || lower.Contains("no address associated"))
                return new DownloadException("Couldn't reach YouTube.", "Check your internet connection and retry.", false, text);
            if (lower.Contains("http error 403") || lower.Contains("requested format is not available") ||
                lower.Contains("unable to extract") || lower.Contains("signature") || lower.Contains("nsig") ||
                lower.Contains("n challenge") || lower.Contains("precondition") || lower.Contains("only images are available") ||
                lower.Contains("please report this issue"))
                return new DownloadException("YouTube changed something on its side.", "Updating yt-dlp usually fixes this. The app retries after updating.", true, text);
            return new DownloadException(detail.Length == 0 ? "The download failed." : detail, "Retry, or open Settings and select Update tools.", false, text);
        }
    }
}
