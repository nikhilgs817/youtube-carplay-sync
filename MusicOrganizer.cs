using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;

namespace YouTubeMp3
{
    internal sealed class Category
    {
        public const string Unknown = "Unknown";
        public readonly string Genre;
        public readonly string Language;

        public Category(string genre, string language)
        {
            Genre = Normalize(genre);
            Language = Normalize(language);
        }

        private static string Normalize(string name)
        {
            name = (name ?? "").Trim();
            return name.Length == 0 || name.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                ? Unknown : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.ToLowerInvariant());
        }
    }

    internal sealed class TrackMetadata
    {
        public string Filename { get; set; }
        public string Title { get; set; }
        public string Artist { get; set; }
        public string Album { get; set; }
        public string GenreTag { get; set; }
        public string LanguageTag { get; set; }

        public static TrackMetadata Read(string path)
        {
            var track = new TrackMetadata { Filename = Path.GetFileNameWithoutExtension(path) };
            try
            {
                using (var tags = new TrackTags(path))
                {
                    track.Title = tags.Read("System.Title");
                    track.Artist = tags.Read("System.Music.Artist");
                    track.Album = tags.Read("System.Music.AlbumTitle");
                    track.GenreTag = tags.Read("System.Music.Genre");
                    track.LanguageTag = tags.Read("System.Language");
                }
            }
            catch (COMException) { }
            return track;
        }
    }

    internal static class MusicOrganizer
    {
        public const string Genre = "Genre";
        public const string Language = "Language";
        public const string Both = "Language + genre";
        private static readonly HashSet<string> AudioExtensions = new HashSet<string>(
            new[] { ".mp3", ".m4a", ".aac", ".flac", ".wav", ".ogg", ".opus", ".wma", ".aiff", ".alac" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly Regex Reserved = new Regex(@"\A(CON|PRN|AUX|NUL|COM[1-9¹²³]|LPT[1-9¹²³])(\.|$)",
                                                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsAudio(string path) { return AudioExtensions.Contains(Path.GetExtension(path)); }

        public static string FullFolder(string path)
        {
            path = Environment.ExpandEnvironmentVariables((path ?? "").Trim().Trim('"'));
            if (!path.StartsWith(@"\\", StringComparison.Ordinal) &&
                (path.Length < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/')))
                throw new ArgumentException("Choose a full Windows folder path.");
            string full = Path.GetFullPath(path);
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal))
                throw new ArgumentException("Choose a standard Windows folder path.");
            return full;
        }

        public static bool IsInFolder(string path, string folder)
        {
            string prefix = folder.TrimEnd('\\') + "\\";
            return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static string[] Scan(string root, string output, CancellationToken cancel)
        {
            if (String.Equals(root.TrimEnd('\\'), output.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Choose an organized folder separate from the source folder.");
            var files = new List<string>();
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count != 0)
            {
                string directory = pending.Pop();
                cancel.ThrowIfCancellationRequested();
                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (IsAudio(file) && !IsInFolder(file, output)) files.Add(file);
                }
                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if (String.Equals(child, output.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) continue;
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0) pending.Push(child);
                }
            }
            return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        public static string RelativeFolder(Category category, string grouping)
        {
            string genre = SafeName(category.Genre);
            string language = SafeName(category.Language);
            if (grouping == Genre) return genre;
            if (grouping == Language) return language;
            return language + "-" + genre;
        }

        private static string SafeName(string label)
        {
            var invalid = Path.GetInvalidFileNameChars();
            string clean = new string(label.Select(c => Array.IndexOf(invalid, c) >= 0 ? ' ' : c).ToArray());
            clean = Regex.Replace(clean, @"\s+", " ").Trim(' ', '.');
            if (clean.Length > 38)
            {
                int length = Char.IsHighSurrogate(clean[37]) ? 37 : 38;
                clean = clean.Substring(0, length).TrimEnd(' ', '.');
            }
            if (clean.Length == 0) return Category.Unknown;
            return Reserved.IsMatch(clean) ? "_" + clean : clean;
        }

        public static bool Copy(string source, string output, string relativeFolder, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            if (IsInFolder(source, output))
                throw new InvalidOperationException("Choose an output folder outside this track's existing organized folder.");
            string directory = Path.Combine(output, relativeFolder);
            Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".music-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var original = File.OpenRead(source))
                using (var copy = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536];
                    int bytes;
                    while ((bytes = original.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        cancel.ThrowIfCancellationRequested();
                        copy.Write(buffer, 0, bytes);
                    }
                }
                string stem = Path.GetFileNameWithoutExtension(source);
                string extension = Path.GetExtension(source);
                string target = Path.Combine(directory, stem + extension);
                for (int number = 2; ; number++)
                {
                    cancel.ThrowIfCancellationRequested();
                    try { File.Move(temp, target); return true; }
                    catch (IOException)
                    {
                        if (!File.Exists(target)) throw;
                        if (SameFile(temp, target)) return false;
                        target = Path.Combine(directory, stem + " (" + number + ")" + extension);
                    }
                }
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        private static bool SameFile(string first, string second)
        {
            using (var a = File.OpenRead(first))
            using (var b = File.OpenRead(second))
            using (var sha = SHA256.Create())
                return a.Length == b.Length && sha.ComputeHash(a).SequenceEqual(sha.ComputeHash(b));
        }
    }

    // Read existing tags through the same Windows property system used by File Explorer.
    internal sealed class TrackTags : IDisposable
    {
        private IPropertyStore store;

        public TrackTags(string path)
        {
            var id = typeof(IPropertyStore).GUID;
            SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 0u, ref id, out store);
        }

        public string Read(string name)
        {
            try
            {
                PropertyKey key;
                PSGetPropertyKeyFromName(name, out key);
                PropVariant value;
                store.GetValue(ref key, out value);
                try
                {
                    if (value.Type == 0) return null;
                    var text = PropVariantToStringAlloc(ref value);
                    try { return Marshal.PtrToStringUni(text); }
                    finally { Marshal.FreeCoTaskMem(text); }
                }
                finally { PropVariantClear(ref value); }
            }
            catch (COMException) { return null; }
        }

        public void Dispose() { if (store != null) Marshal.ReleaseComObject(store); }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private struct PropertyKey
        {
            public Guid Format;
            public uint Id;
        }

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort Type;
        }

        [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            void GetCount(out uint count);
            void GetAt(uint index, out PropertyKey key);
            void GetValue(ref PropertyKey key, out PropVariant value);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, uint flags,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

        [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void PSGetPropertyKeyFromName(string name, out PropertyKey key);

        [DllImport("propsys.dll", PreserveSig = false)]
        private static extern IntPtr PropVariantToStringAlloc(ref PropVariant value);

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);
    }
}
