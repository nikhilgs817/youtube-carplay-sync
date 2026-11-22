using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

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

    internal sealed class DownloadTools
    {
        public readonly string Downloader;
        public readonly string Encoder;
        public readonly string JsRuntime;

        public DownloadTools()
        {
            Downloader = Find("yt-dlp.exe");
            Encoder = Find("ffmpeg.exe");
            string deno = Find("deno.exe");
            string node = deno == null ? Find("node.exe") : null;
            JsRuntime = deno != null ? "deno:" + deno : node == null ? null : "node:" + node;
        }

        private static string Find(string executable)
        {
            string appFolder = AppDomain.CurrentDomain.BaseDirectory;
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] locations = {
                Path.Combine(appFolder, "tools"), appFolder,
                Path.Combine(local, "Microsoft", "WinGet", "Links"),
                Path.Combine(profile, ".deno", "bin"),
                Environment.GetEnvironmentVariable("PATH"),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
                Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine)
            };
            foreach (string location in locations)
            {
                if (String.IsNullOrWhiteSpace(location)) continue;
                foreach (string entry in location.Split(';'))
                {
                    string folder = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                    if (folder.Length < 3 || !Path.IsPathRooted(folder) || folder[1] != ':') continue;
                    string candidate = Path.Combine(folder, executable);
                    if (File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }
    }

    internal static class AudioDownload
    {
        private const string ProgressPrefix = "MP3_PROGRESS:";
        private const string FilePrefix = "MP3_FILE:";

        public static string Save(VideoLink link, string destination, DownloadTools tools,
                                  Action<int, string> report, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (link.Id == null) throw new ArgumentException("Enter a YouTube video URL.");
            if (tools.Downloader == null || tools.Encoder == null || tools.JsRuntime == null)
                throw new InvalidOperationException("Install yt-dlp, FFmpeg and Deno with Setup tools.");

            string stage = Path.Combine(destination, ".mp3-work-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            try
            {
                string[] arguments = {
                    "--ignore-config", "--no-plugin-dirs", "--no-playlist", "--no-simulate",
                    "--newline", "--progress", "--progress-delta", "0.4", "--color", "never",
                    "--progress-template", "download:" + ProgressPrefix + "%(progress._percent_str)s",
                    "--print", "after_move:" + FilePrefix + "%(filepath)s",
                    "--windows-filenames", "--socket-timeout", "20", "--retries", "3",
                    "--ffmpeg-location", Path.GetDirectoryName(tools.Encoder),
                    "--no-js-runtimes", "--js-runtimes", tools.JsRuntime,
                    "-f", "bestaudio/best", "--extract-audio", "--audio-format", "mp3",
                    "--audio-quality", "0", "-P", stage, "-o", "%(title).120B [%(id)s].%(ext)s",
                    "--", "https://www.youtube.com/watch?v=" + link.Id
                };
                string path = Run(tools.Downloader, arguments, report, cancel);
                if (String.IsNullOrEmpty(path) || !Path.IsPathRooted(path) ||
                    !String.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), stage, StringComparison.OrdinalIgnoreCase) ||
                    !String.Equals(Path.GetExtension(path), ".mp3", StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("The converter did not produce an MP3 file.");

                cancel.ThrowIfCancellationRequested();
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
                        if (!File.Exists(target)) throw;
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

        private static string Run(string executable, string[] arguments, Action<int, string> report,
                                  CancellationToken cancel)
        {
            var info = new ProcessStartInfo(executable, String.Join(" ", Array.ConvertAll(arguments, Quote)))
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.EnvironmentVariables["PYTHONIOENCODING"] = "utf-8";
            string path = null;
            string error = null;
            using (var process = new Process { StartInfo = info })
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    string line = e.Data;
                    if (line == null) return;
                    if (line.StartsWith(FilePrefix, StringComparison.Ordinal))
                        path = line.Substring(FilePrefix.Length);
                    if (!line.StartsWith(ProgressPrefix, StringComparison.Ordinal)) return;
                    double percent;
                    if (Double.TryParse(line.Substring(ProgressPrefix.Length).Trim().TrimEnd('%'),
                                        NumberStyles.Float, CultureInfo.InvariantCulture, out percent))
                    {
                        if (percent >= 100) report(100, "Converting to MP3…");
                        else if (percent >= 0) report((int)percent, "Downloading audio…");
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (!String.IsNullOrWhiteSpace(e.Data) &&
                        (error == null || e.Data.StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase)))
                        error = e.Data.Trim();
                };
                cancel.ThrowIfCancellationRequested();
                process.Start();
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
                if (process.ExitCode != 0)
                    throw new InvalidOperationException(error ?? "The downloader exited with code " + process.ExitCode + ".");
            }
            return path;
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
}
