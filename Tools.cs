using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace YouTubeMp3
{
    // Finds the audio utilities. The app's own copies (installed and kept current by ToolManager) win over WinGet,
    // PATH or a tools folder, so an old yt-dlp elsewhere on the PC can no longer break downloads.
    internal sealed class DownloadTools
    {
        public static readonly string ManagedFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeMP3", "tools");
        public readonly string Downloader;
        public readonly string Encoder;
        public readonly string JsRuntime;
        public readonly bool ManagedDownloader;

        public DownloadTools()
        {
            Downloader = Find("yt-dlp.exe");
            ManagedDownloader = Downloader != null &&
                String.Equals(Path.GetDirectoryName(Downloader), ManagedFolder, StringComparison.OrdinalIgnoreCase);
            Encoder = FindEncoder();
            string deno = Find("deno.exe");
            string node = deno == null ? Find("node.exe") : null;
            JsRuntime = deno != null ? "deno:" + deno : node == null ? null : "node:" + node;
        }

        // For automated tests: use these exact executables.
        internal DownloadTools(string downloader, string encoder, string jsRuntime)
        {
            Downloader = downloader;
            Encoder = encoder;
            JsRuntime = jsRuntime;
        }

        public bool Ready { get { return Downloader != null && Encoder != null; } }

        public string Missing
        {
            get
            {
                if (Downloader == null && Encoder == null) return "yt-dlp and FFmpeg";
                if (Downloader == null) return "yt-dlp";
                if (Encoder == null) return "FFmpeg";
                return null;
            }
        }

        private static string[] Locations()
        {
            string appFolder = AppDomain.CurrentDomain.BaseDirectory;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] {
                ManagedFolder, Path.Combine(appFolder, "tools"), appFolder,
                Path.Combine(local, "Microsoft", "WinGet", "Links"),
                Path.Combine(profile, ".deno", "bin"),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
            };
        }

        private static string Find(string executable, string companion = null)
        {
            foreach (string location in Locations())
            {
                if (String.IsNullOrWhiteSpace(location)) continue;
                foreach (string entry in location.Split(';'))
                {
                    string folder = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                    if (folder.Length < 3 || !Path.IsPathRooted(folder) || folder[1] != ':') continue;
                    string candidate = Path.Combine(folder, executable);
                    if (!File.Exists(candidate)) continue;
                    if (companion != null && !File.Exists(Path.Combine(folder, companion))) continue;
                    return candidate;
                }
            }
            return null;
        }

        // yt-dlp needs ffprobe next to ffmpeg to convert audio, so prefer a folder that has both.
        private static string FindEncoder()
        {
            return Find("ffmpeg.exe", "ffprobe.exe") ?? Find("ffmpeg.exe");
        }
    }

    internal sealed class ToolProgress
    {
        internal string Message;
        internal int Percent = -1;
        internal ToolProgress(string message, int percent) { Message = message; Percent = percent; }
    }

    internal static class ToolManager
    {
        private const string DownloaderUrl = "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe";
        private const string EncoderUrl = "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl-shared.zip";
        private const string DenoUrl = "https://github.com/denoland/deno/releases/latest/download/deno-x86_64-pc-windows-msvc.zip";
        private static int busy;

        static ToolManager()
        {
            // The compiler-built EXE has no target-framework attribute, so .NET would otherwise offer old TLS only.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
        }

        internal static bool IsBusy { get { return busy != 0; } }

        // Installs what is missing into the app's own tools folder. With refresh, replaces every tool with the latest.
        internal static async Task<string> InstallAsync(bool refresh, IProgress<ToolProgress> progress, CancellationToken cancel)
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) throw new InvalidOperationException("Tool setup is already running.");
            try
            {
                Directory.CreateDirectory(DownloadTools.ManagedFolder);
                string folder = DownloadTools.ManagedFolder;
                var done = new StringBuilder();
                using (var http = Client())
                {
                    string ytdlp = Path.Combine(folder, "yt-dlp.exe");
                    if (refresh || !File.Exists(ytdlp))
                    {
                        await DownloadFile(http, DownloaderUrl, ytdlp, "Downloading yt-dlp", progress, cancel).ConfigureAwait(false);
                        done.Append("yt-dlp ");
                    }
                    if (refresh || !File.Exists(Path.Combine(folder, "ffmpeg.exe")) || !File.Exists(Path.Combine(folder, "ffprobe.exe")))
                    {
                        await DownloadZip(http, EncoderUrl, folder, new[] { "ffmpeg.exe", "ffprobe.exe" }, "Downloading FFmpeg",
                            progress, cancel).ConfigureAwait(false);
                        done.Append("FFmpeg ");
                    }
                    if (refresh || !File.Exists(Path.Combine(folder, "deno.exe")))
                    {
                        try
                        {
                            await DownloadZip(http, DenoUrl, folder, new[] { "deno.exe" }, "Downloading Deno", progress, cancel)
                                .ConfigureAwait(false);
                            done.Append("Deno ");
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            // yt-dlp still works without a JavaScript runtime for many videos; report and continue.
                            done.Append("(Deno skipped: " + ex.Message + ") ");
                        }
                    }
                }
                Report(progress, "Checking tools", -1);
                string version = await Task.Run(() => Version(Path.Combine(folder, "yt-dlp.exe"), "--version")).ConfigureAwait(false);
                if (version == null) throw new InvalidOperationException("yt-dlp was downloaded but does not start. Check your antivirus quarantine.");
                Stamp();
                return done.Length == 0 ? "Tools are already installed." : "Installed " + done.ToString().Trim() + ". yt-dlp " + version + ".";
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        // Updates yt-dlp in place. Returns the new version, or null when nothing could be updated.
        internal static async Task<string> UpdateDownloaderAsync(IProgress<ToolProgress> progress, CancellationToken cancel)
        {
            var tools = new DownloadTools();
            if (!tools.ManagedDownloader)
            {
                await InstallAsync(false, progress, cancel).ConfigureAwait(false);
                tools = new DownloadTools();
                if (!tools.ManagedDownloader) return null;
            }
            if (Interlocked.Exchange(ref busy, 1) == 1) return null;
            try
            {
                Report(progress, "Updating yt-dlp", -1);
                string before = Version(tools.Downloader, "--version");
                bool ok = await Task.Run(() => RunQuiet(tools.Downloader, "--update-to stable", 120000, cancel)).ConfigureAwait(false);
                string after = Version(tools.Downloader, "--version");
                if (!ok || after == null)
                {
                    using (var http = Client())
                        await DownloadFile(http, DownloaderUrl, tools.Downloader, "Downloading yt-dlp", progress, cancel).ConfigureAwait(false);
                    after = Version(tools.Downloader, "--version");
                }
                Stamp();
                return after == null ? null : (before == after ? after + " (already current)" : after);
            }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        private static string StampFile { get { return Path.Combine(DownloadTools.ManagedFolder, "last-update.txt"); } }

        private static void Stamp()
        {
            try { File.WriteAllText(StampFile, DateTime.UtcNow.ToString("o")); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // True when the app's yt-dlp has not been refreshed for most of a day. YouTube changes often.
        internal static bool UpdateDue()
        {
            if (!new DownloadTools().ManagedDownloader) return false;
            try
            {
                DateTime last;
                return !File.Exists(StampFile) || !DateTime.TryParse(File.ReadAllText(StampFile).Trim(), null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out last) || DateTime.UtcNow - last > TimeSpan.FromHours(20);
            }
            catch (IOException) { return true; }
            catch (UnauthorizedAccessException) { return true; }
        }

        internal static string Version(string executable, string argument)
        {
            if (executable == null || !File.Exists(executable)) return null;
            try
            {
                var info = new ProcessStartInfo(executable, argument)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(info))
                {
                    var text = process.StandardOutput.ReadToEndAsync();
                    process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(20000)) { try { process.Kill(); } catch (InvalidOperationException) { } return null; }
                    string first = (text.GetAwaiter().GetResult() ?? "").Trim().Split('\n')[0].Trim();
                    if (first.StartsWith("ffmpeg version ", StringComparison.OrdinalIgnoreCase))
                        first = first.Substring(15).Split(' ')[0];
                    if (first.StartsWith("deno ", StringComparison.OrdinalIgnoreCase)) first = first.Substring(5).Split(' ')[0];
                    return first.Length == 0 ? null : first;
                }
            }
            catch (Exception ex)
            {
                if (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is IOException) return null;
                throw;
            }
        }

        private static bool RunQuiet(string executable, string arguments, int timeout, CancellationToken cancel)
        {
            var info = new ProcessStartInfo(executable, arguments)
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                process.StandardOutput.ReadToEndAsync();
                process.StandardError.ReadToEndAsync();
                var watch = Stopwatch.StartNew();
                while (!process.WaitForExit(200))
                {
                    if (cancel.IsCancellationRequested || watch.ElapsedMilliseconds > timeout)
                    {
                        try { process.Kill(); } catch (InvalidOperationException) { }
                        cancel.ThrowIfCancellationRequested();
                        return false;
                    }
                }
                return process.ExitCode == 0;
            }
        }

        private static HttpClient Client()
        {
            var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeCarPlaySync/" + AppInfo.Version);
            return http;
        }

        private static void Report(IProgress<ToolProgress> progress, string message, int percent)
        {
            if (progress != null) progress.Report(new ToolProgress(message, percent));
        }

        private static async Task<string> DownloadTemp(HttpClient http, string url, string folder, string label,
            IProgress<ToolProgress> progress, CancellationToken cancel)
        {
            string temp = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + ".download");
            try
            {
                Report(progress, label, -1);
                using (var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(label + " failed: GitHub returned HTTP " + (int)response.StatusCode + ".");
                    long total = response.Content.Headers.ContentLength ?? -1;
                    using (var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        int lastPercent = -2, read;
                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancel).ConfigureAwait(false)) > 0)
                        {
                            await file.WriteAsync(buffer, 0, read, cancel).ConfigureAwait(false);
                            received += read;
                            int percent = total > 0 ? (int)(100 * received / total) : -1;
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                Report(progress, label + (total > 0 ? " · " + (received >> 20) + " of " + (total >> 20) + " MB" : ""), percent);
                            }
                        }
                        if (total > 0 && received != total) throw new IOException(label + " was interrupted. Try again.");
                    }
                }
                return temp;
            }
            catch
            {
                TryDelete(temp);
                throw;
            }
        }

        private static async Task DownloadFile(HttpClient http, string url, string target, string label,
            IProgress<ToolProgress> progress, CancellationToken cancel)
        {
            string temp = await DownloadTemp(http, url, Path.GetDirectoryName(target), label, progress, cancel).ConfigureAwait(false);
            try { Replace(temp, target); }
            finally { TryDelete(temp); }
        }

        private static async Task DownloadZip(HttpClient http, string url, string folder, string[] names, string label,
            IProgress<ToolProgress> progress, CancellationToken cancel)
        {
            string temp = await DownloadTemp(http, url, folder, label, progress, cancel).ConfigureAwait(false);
            try
            {
                Report(progress, "Unpacking " + String.Join(" and ", names), -1);
                await Task.Run(delegate
                {
                    using (var archive = ZipFile(temp))
                    {
                        foreach (string name in names)
                        {
                            cancel.ThrowIfCancellationRequested();
                            ZipArchiveEntry match = null;
                            foreach (var entry in archive.Entries)
                                if (String.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase) &&
                                    (match == null || entry.FullName.Length < match.FullName.Length)) match = entry;
                            if (match == null) throw new InvalidDataException(label + " did not contain " + name + ".");
                            Extract(match, Path.Combine(folder, name));
                            // A shared FFmpeg build needs the DLLs that sit beside its executables.
                            string home = match.FullName.Substring(0, match.FullName.Length - match.Name.Length);
                            foreach (var entry in archive.Entries)
                                if (entry.FullName.StartsWith(home, StringComparison.Ordinal) && entry.FullName.IndexOf('/', home.Length) < 0 &&
                                    entry.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) && entry.Name == Path.GetFileName(entry.Name))
                                    Extract(entry, Path.Combine(folder, entry.Name));
                        }
                    }
                }, cancel).ConfigureAwait(false);
            }
            finally { TryDelete(temp); }
        }

        // Writes one archive entry to a name chosen here, so archive paths can never escape the tools folder.
        private static void Extract(ZipArchiveEntry entry, string target)
        {
            if (Path.GetDirectoryName(target) != DownloadTools.ManagedFolder.TrimEnd(Path.DirectorySeparatorChar) &&
                !String.Equals(Path.GetDirectoryName(target), DownloadTools.ManagedFolder, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected file in the tool archive.");
            string part = Path.Combine(Path.GetDirectoryName(target), "." + Guid.NewGuid().ToString("N") + ".part");
            try
            {
                using (var input = entry.Open())
                using (var output = new FileStream(part, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(output);
                Replace(part, target);
            }
            finally { TryDelete(part); }
        }

        private static ZipArchive ZipFile(string path)
        {
            return new ZipArchive(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ZipArchiveMode.Read, false);
        }

        private static void Replace(string source, string target)
        {
            if (File.Exists(target))
            {
                string old = target + ".old";
                TryDelete(old);
                try { File.Replace(source, target, old, true); }
                catch (PlatformNotSupportedException) { File.Delete(target); File.Move(source, target); }
                TryDelete(old);
            }
            else File.Move(source, target);
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
