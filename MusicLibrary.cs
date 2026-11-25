using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Xml.Linq;

namespace YouTubeMp3
{
    internal sealed class Track
    {
        internal string Path, RelativePath, Title, Artist, Language, Genre;
        internal bool WriteTitle, WriteArtist;
        internal TrackMetadata Metadata;
        internal string Group
        {
            get
            {
                return (Language.Length == 0 ? "Unspecified" : Language) + " - " +
                    (Genre.Length == 0 ? "Unspecified" : Genre);
            }
        }
    }

    internal sealed class MusicLibrary
    {
        internal static readonly string CatalogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YouTubeMP3", "Catalogs");
        private static readonly string LegacyCatalogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mulluman", "Catalogs");

        // 1.2 kept saved tags under an old prototype name. Copy them once so no edits are lost; the old files stay.
        internal static void MigrateCatalogs()
        {
            try
            {
                if (!Directory.Exists(LegacyCatalogDirectory)) return;
                Directory.CreateDirectory(CatalogDirectory);
                foreach (var file in Directory.EnumerateFiles(LegacyCatalogDirectory, "*.xml"))
                {
                    var target = Path.Combine(CatalogDirectory, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        internal readonly string Root;
        internal readonly List<Track> Tracks = new List<Track>();
        internal readonly List<string> Warnings = new List<string>();
        private readonly string catalogFile;

        internal MusicLibrary(string source, string catalogDirectory, CancellationToken cancellation = default(CancellationToken),
            string excludedFolder = null)
        {
            Root = Path.GetFullPath(source).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            using (var hash = SHA256.Create())
            {
                var key = hash.ComputeHash(Encoding.UTF8.GetBytes(Root.ToUpperInvariant()));
                catalogFile = Path.Combine(catalogDirectory, BitConverter.ToString(key).Replace("-", "") + ".xml");
            }

            var saved = File.Exists(catalogFile)
                ? XDocument.Load(catalogFile).Root.Elements("track").ToDictionary(
                    row => (string)row.Attribute("path"), StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);

            var folders = new Stack<string>();
            folders.Push(Root);
            while (folders.Count != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                var folder = folders.Pop();
                try
                {
                    foreach (var path in Directory.EnumerateFileSystemEntries(folder))
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var attributes = File.GetAttributes(path);
                        if (attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                        if (attributes.HasFlag(FileAttributes.Directory))
                        {
                            if (!String.Equals(path, excludedFolder, StringComparison.OrdinalIgnoreCase)) folders.Push(path);
                        }
                        else if (MusicOrganizer.IsAudio(path))
                            Tracks.Add(ReadTrack(path, saved));
                    }
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException)) throw;
                    Warnings.Add(folder + " — " + error.Message);
                }
            }
            Tracks.Sort((first, second) => StringComparer.CurrentCultureIgnoreCase.Compare(first.Title, second.Title));
        }

        private Track ReadTrack(string path, Dictionary<string, XElement> saved)
        {
            var metadata = TrackMetadata.Read(path);
            var track = new Track
            {
                Path = path, RelativePath = path.Substring(Root.Length),
                Title = String.IsNullOrWhiteSpace(metadata.Title) ? metadata.Filename : metadata.Title,
                Artist = metadata.Artist ?? "", Language = CleanTag(metadata.LanguageTag), Genre = CleanGenreTag(metadata.GenreTag),
                WriteTitle = String.IsNullOrWhiteSpace(metadata.Title), Metadata = metadata
            };
            XElement row;
            if (saved.TryGetValue(track.RelativePath, out row))
            {
                track.Title = (string)row.Element("title") ?? track.Title;
                track.Artist = (string)row.Element("artist") ?? "";
                track.Language = (string)row.Element("language") ?? "";
                track.Genre = CleanGenreTag((string)row.Element("genre"));
                track.WriteTitle = (bool?)row.Attribute("writeTitle") ?? false;
                track.WriteArtist = (bool?)row.Attribute("writeArtist") ?? false;
                return track;
            }

            var grouped = track.Genre.Split(new[] { " - " }, StringSplitOptions.None);
            if (grouped.Length == 2)
            {
                track.Language = CleanTag(grouped[0]);
                track.Genre = CleanTag(grouped[1]);
            }
            return track;
        }

        // yt-dlp's --embed-metadata writes the YouTube video category (almost always "Music") as the genre.
        // That is not a music genre, so treat it as blank and let the user or the AI fill a real one.
        private static readonly string[] YouTubeCategories = {
            "Music", "Entertainment", "People & Blogs", "Film & Animation", "Gaming", "Education", "Comedy",
            "Howto & Style", "News & Politics", "Nonprofits & Activism", "Science & Technology", "Sports",
            "Travel & Events", "Autos & Vehicles", "Pets & Animals", "Shows", "Trailers" };

        internal static string CleanGenreTag(string value)
        {
            value = CleanTag(value);
            foreach (string category in YouTubeCategories)
                if (String.Equals(value, category, StringComparison.OrdinalIgnoreCase)) return "";
            return value;
        }

        internal static string CleanTag(string value)
        {
            value = (value ?? "").Trim();
            return String.Equals(value, "Unspecified", StringComparison.OrdinalIgnoreCase) ||
                String.Equals(value, Category.Unknown, StringComparison.OrdinalIgnoreCase) ? "" : value;
        }

        internal void Save()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(catalogFile));
            var temporary = Path.Combine(Path.GetDirectoryName(catalogFile), "." + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                new XDocument(new XElement("library", Tracks.Select(track =>
                    new XElement("track", new XAttribute("path", track.RelativePath),
                        new XAttribute("writeTitle", track.WriteTitle), new XAttribute("writeArtist", track.WriteArtist),
                        new XElement("title", track.Title), new XElement("artist", track.Artist),
                        new XElement("language", track.Language), new XElement("genre", track.Genre))))).Save(temporary);
                if (File.Exists(catalogFile)) File.Replace(temporary, catalogFile, null);
                else File.Move(temporary, catalogFile);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal ExportResult Export(string destination, IEnumerable<Track> selection = null,
            CancellationToken cancellation = default(CancellationToken), string encoder = null)
        {
            var fullDestination = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullDestination.StartsWith(Root, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Choose an export location outside your source music folder.");

            var folder = Path.Combine(fullDestination, "CarPlay Music " + Guid.NewGuid().ToString("N").Substring(0, 8));
            var audioFolder = Path.Combine(folder, "Music");
            Directory.CreateDirectory(audioFolder);
            var result = new ExportResult { Directory = folder };
            var exported = new List<Track>();

            foreach (var track in selection ?? Tracks)
            {
                if (cancellation.IsCancellationRequested) break;
                var output = AvailablePath(Path.Combine(audioFolder, Path.ChangeExtension(track.RelativePath, ".mp3")));
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(output));
                    bool converted = !String.Equals(Path.GetExtension(track.Path), ".mp3", StringComparison.OrdinalIgnoreCase);
                    if (converted) ConvertTrack(track.Path, output, encoder, cancellation);
                    else File.Copy(track.Path, output);
                    File.SetAttributes(output, File.GetAttributes(output) & ~FileAttributes.ReadOnly);
                    using (var tags = new Mp3Tags(output, true))
                        tags.Save(converted || track.WriteTitle ? track.Title : null,
                                  converted || track.WriteArtist ? track.Artist : null, track.Group);
                    exported.Add(track);
                }
                catch (OperationCanceledException)
                {
                    RemovePartial(output, result);
                    break;
                }
                catch (Exception error)
                {
                    if (!(error is IOException) && !(error is UnauthorizedAccessException) &&
                        !(error is System.Runtime.InteropServices.COMException) && !(error is Win32Exception)) throw;
                    result.Failures.Add(track.RelativePath + " — " + error.Message);
                    RemovePartial(output, result);
                }
            }

            result.Count = exported.Count;
            result.Cancelled = cancellation.IsCancellationRequested;
            var rules = new List<string> { "Use Genre / is for each rule. For playlists with multiple rules, set Match to any.", "" };
            AddRules(rules, "Language", exported.Where(track => track.Language.Length > 0), track => track.Language);
            AddRules(rules, "Genre", exported.Where(track => track.Genre.Length > 0), track => track.Genre);
            AddRules(rules, "Language + genre", exported.Where(track => track.Language.Length > 0 && track.Genre.Length > 0),
                track => track.Language + " " + track.Genre);
            File.WriteAllLines(Path.Combine(folder, "Playlist-rules.txt"), rules, Encoding.UTF8);
            if (result.Failures.Count > 0) File.WriteAllLines(Path.Combine(folder, "Export-errors.txt"), result.Failures, Encoding.UTF8);
            return result;
        }

        private static void RemovePartial(string path, ExportResult result)
        {
            if (!File.Exists(path)) return;
            try { File.Delete(path); }
            catch (IOException) { result.Failures.Add("Check this partial copy before import: " + path); }
            catch (UnauthorizedAccessException) { result.Failures.Add("Check this partial copy before import: " + path); }
        }

        private static string AvailablePath(string path)
        {
            string candidate = path;
            string stem = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path));
            for (int suffix = 2; File.Exists(candidate) || Directory.Exists(candidate); suffix++)
                candidate = stem + " (" + suffix + ").mp3";
            return candidate;
        }

        private static void ConvertTrack(string source, string output, string encoder, CancellationToken cancellation)
        {
            if (String.IsNullOrEmpty(encoder)) throw new IOException("Install FFmpeg using Setup tools to export this audio format.");
            var info = new ProcessStartInfo(encoder, "-nostdin -hide_banner -loglevel error -n -i \"" + source +
                "\" -map 0:a:0 -vn -codec:a libmp3lame -q:a 2 \"" + output + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
            using (var process = Process.Start(info))
            {
                var error = process.StandardError.ReadToEndAsync();
                while (!process.WaitForExit(150))
                {
                    if (!cancellation.IsCancellationRequested) continue;
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    process.WaitForExit();
                    cancellation.ThrowIfCancellationRequested();
                }
                if (process.ExitCode != 0)
                    throw new IOException("FFmpeg could not convert this track. " + error.GetAwaiter().GetResult().Trim());
            }
        }
        private static void AddRules(List<string> lines, string heading, IEnumerable<Track> tracks, Func<Track, string> select)
        {
            lines.Add(heading);
            foreach (var group in tracks.GroupBy(select, StringComparer.OrdinalIgnoreCase).OrderBy(group => group.Key))
            {
                lines.Add("  " + group.Key + " (" + group.Count() + " songs)");
                lines.AddRange(group.Select(track => track.Group).Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(genre => genre).Select(genre => "    Genre is " + genre));
            }
            lines.Add("");
        }
    }

    internal sealed class ExportResult
    {
        internal string Directory;
        internal int Count;
        internal bool Cancelled;
        internal readonly List<string> Failures = new List<string>();
    }
}
