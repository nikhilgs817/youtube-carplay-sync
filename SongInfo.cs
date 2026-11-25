using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace YouTubeMp3
{
    // What YouTube itself knows about a video. Official music uploads often list the song, artist and album
    // ("Music in this video"); yt-dlp returns those as track, artist and album.
    internal sealed class YouTubeInfo
    {
        internal string Title = "", Channel = "", Track = "", Artist = "", Album = "", Year = "", Description = "";

        internal static YouTubeInfo Parse(string json)
        {
            var info = new YouTubeInfo();
            if (String.IsNullOrWhiteSpace(json)) return info;
            IDictionary<string, object> map;
            try { map = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.DeserializeObject(json) as IDictionary<string, object>; }
            catch (ArgumentException) { return info; }
            catch (InvalidOperationException) { return info; }
            if (map == null) return info;
            info.Title = Text(map, "title");
            info.Channel = Text(map, "channel");
            if (info.Channel.Length == 0) info.Channel = Text(map, "uploader");
            info.Track = Text(map, "track");
            info.Artist = Text(map, "artist");
            if (info.Artist.Length == 0)
            {
                var artists = Value(map, "artists") as object[];
                if (artists != null) info.Artist = String.Join(", ", artists.Select(a => Convert.ToString(a, CultureInfo.InvariantCulture)).ToArray());
            }
            info.Album = Text(map, "album");
            info.Year = Text(map, "release_year");
            string description = Text(map, "description");
            info.Description = description.Length > 900 ? description.Substring(0, 900) : description;
            return info;
        }

        private static object Value(IDictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) ? value : null;
        }

        private static string Text(IDictionary<string, object> map, string key)
        {
            var value = Value(map, key);
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture).Trim();
        }
    }

    // One song in Apple's catalogue (the iTunes Search API, the catalogue Apple Music itself uses).
    internal sealed class CatalogMatch
    {
        internal string Title = "", Artist = "", Album = "", AlbumArtist = "", Year = "", Genre = "", Artwork = "", Source = "Apple Music";
        internal int Track, TrackCount;
    }

    internal static class SongLookup
    {
        private static readonly object Gate = new object();
        private static DateTime nextCall = DateTime.MinValue;

        // Apple asks for about 20 searches a minute at most, so calls are spaced 3.2 seconds apart.
        internal static async Task<List<CatalogMatch>> SearchAppleAsync(string term, string country, CancellationToken cancel)
        {
            TimeSpan wait;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                var slot = nextCall > now ? nextCall : now;
                wait = slot - now;
                nextCall = slot.AddSeconds(3.2);
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeCarPlaySync/" + AppInfo.Version);
                string url = "https://itunes.apple.com/search?media=music&entity=song&limit=12&country=" + country +
                    "&term=" + Uri.EscapeDataString(term);
                using (var response = await http.GetAsync(url, cancel))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Apple's music catalogue answered HTTP " + (int)response.StatusCode + ".");
                    return ParseApple(await response.Content.ReadAsStringAsync());
                }
            }
        }

        internal static List<CatalogMatch> ParseApple(string json)
        {
            var list = new List<CatalogMatch>();
            var map = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.DeserializeObject(json) as IDictionary<string, object>;
            object raw;
            if (map == null || !map.TryGetValue("results", out raw) || !(raw is object[])) return list;
            foreach (var item in (object[])raw)
            {
                var r = item as IDictionary<string, object>;
                if (r == null) continue;
                var m = new CatalogMatch
                {
                    Title = Field(r, "trackName"), Artist = Field(r, "artistName"), Album = Field(r, "collectionName"),
                    AlbumArtist = Field(r, "collectionArtistName"), Genre = Field(r, "primaryGenreName"),
                    Artwork = Field(r, "artworkUrl100").Replace("100x100bb", "1000x1000bb")
                };
                if (m.AlbumArtist.Length == 0) m.AlbumArtist = m.Artist;
                string date = Field(r, "releaseDate");
                m.Year = date.Length >= 4 ? date.Substring(0, 4) : "";
                int n;
                if (Int32.TryParse(Field(r, "trackNumber"), out n)) m.Track = n;
                if (Int32.TryParse(Field(r, "trackCount"), out n)) m.TrackCount = n;
                if (m.Title.Length > 0 && m.Artist.Length > 0) list.Add(m);
            }
            return list;
        }

        private static string Field(IDictionary<string, object> map, string key)
        {
            object value;
            return map.TryGetValue(key, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture).Trim() : "";
        }

        private static DateTime nextBrainz = DateTime.MinValue;

        // MusicBrainz, the open music encyclopedia: a fallback for songs Apple's catalogue doesn't have.
        // It asks for one request a second and an app name in the User-Agent.
        internal static async Task<List<CatalogMatch>> SearchMusicBrainzAsync(string term, CancellationToken cancel)
        {
            TimeSpan wait;
            lock (Gate)
            {
                var now = DateTime.UtcNow;
                var slot = nextBrainz > now ? nextBrainz : now;
                wait = slot - now;
                nextBrainz = slot.AddSeconds(1.2);
            }
            if (wait > TimeSpan.Zero) await Task.Delay(wait, cancel);
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeCarPlaySync/" + AppInfo.Version + " (personal Windows app)");
                string url = "https://musicbrainz.org/ws/2/recording?fmt=json&limit=8&query=" + Uri.EscapeDataString(term);
                using (var response = await http.GetAsync(url, cancel))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("MusicBrainz answered HTTP " + (int)response.StatusCode + ".");
                    return ParseMusicBrainz(await response.Content.ReadAsStringAsync());
                }
            }
        }

        internal static List<CatalogMatch> ParseMusicBrainz(string json)
        {
            var list = new List<CatalogMatch>();
            var map = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.DeserializeObject(json) as IDictionary<string, object>;
            object raw;
            if (map == null || !map.TryGetValue("recordings", out raw) || !(raw is object[])) return list;
            foreach (var item in (object[])raw)
            {
                var r = item as IDictionary<string, object>;
                if (r == null) continue;
                var m = new CatalogMatch { Title = Field(r, "title"), Source = "MusicBrainz" };
                object credits;
                if (r.TryGetValue("artist-credit", out credits) && credits is object[])
                {
                    var sb = new StringBuilder();
                    foreach (var c in (object[])credits)
                    {
                        var credit = c as IDictionary<string, object>;
                        if (credit == null) continue;
                        sb.Append(Field(credit, "name")).Append(Field(credit, "joinphrase").Length > 0 ? Convert.ToString(credit["joinphrase"]) : "");
                    }
                    m.Artist = sb.ToString().Trim();
                }
                string date = Field(r, "first-release-date");
                m.Year = date.Length >= 4 ? date.Substring(0, 4) : "";
                object releases;
                if (r.TryGetValue("releases", out releases) && releases is object[] && ((object[])releases).Length > 0)
                {
                    var release = ((object[])releases)[0] as IDictionary<string, object>;
                    if (release != null)
                    {
                        m.Album = Field(release, "title");
                        string id = Field(release, "id");
                        if (id.Length > 0) m.Artwork = "https://coverartarchive.org/release/" + id + "/front-1200";
                    }
                }
                m.AlbumArtist = m.Artist;
                if (m.Title.Length > 0 && m.Artist.Length > 0) list.Add(m);
            }
            return list;
        }

        // Ollama's web search (needs a free Ollama account key): a few page titles and snippets about the song,
        // for the local model to read when the catalogues have nothing.
        internal static async Task<List<string>> WebSearchAsync(string key, string query, CancellationToken cancel)
        {
            var snippets = new List<string>();
            if (String.IsNullOrWhiteSpace(key)) return snippets;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://ollama.com/api/web_search"))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", key.Trim());
                request.Content = new StringContent(new JavaScriptSerializer().Serialize(new { query = query, max_results = 4 }), Encoding.UTF8, "application/json");
                using (var response = await http.SendAsync(request, cancel))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        int code = (int)response.StatusCode;
                        string why = code == 401 || code == 403 ? "the key was refused: check it in Settings"
                            : code == 402 ? "your Ollama account has no web searches available (check your plan or usage on ollama.com)"
                            : code == 429 ? "too many searches: try again later"
                            : "it answered HTTP " + code;
                        throw new InvalidOperationException("Web search unavailable: " + why + " (HTTP " + code + ")");
                    }
                    var map = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue }.DeserializeObject(await response.Content.ReadAsStringAsync()) as IDictionary<string, object>;
                    object raw;
                    if (map == null || !map.TryGetValue("results", out raw) || !(raw is object[])) return snippets;
                    foreach (var item in (object[])raw)
                    {
                        var r = item as IDictionary<string, object>;
                        if (r == null) continue;
                        string content = Field(r, "content");
                        if (content.Length > 500) content = content.Substring(0, 500);
                        snippets.Add(Field(r, "title") + " \u2014 " + content);
                    }
                }
            }
            return snippets;
        }

        private static readonly Regex Noise = new Regex(
            @"\((?:[^()]*?(?:official|video|audio|lyric|full song|hd|4k|visuali[sz]er|song|teaser|trailer|remaster)[^()]*)\)|" +
            @"\[(?:[^\[\]]*)\]|\b(?:official (?:music |lyric(?:al)? )?(?:video|audio)|full (?:video |audio )?song(?: with lyrics)?|" +
            @"with lyrics|lyrics?|lyrical|music video|video song|audio song|4k|hd|visuali[sz]er)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        // "Kesariya - Brahmāstra | Ranbir Kapoor, Alia Bhatt | Pritam | 4K" becomes "Kesariya - Brahmāstra".
        internal static string CleanTitle(string title)
        {
            string text = (title ?? "").Replace('“', '"').Replace('”', '"').Replace('｜', '|');
            int bar = text.IndexOf('|');
            if (bar > 0) text = text.Substring(0, bar);
            text = Noise.Replace(text, " ").Replace("\"", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim(' ', '-', '–', '—', ':', ',');
            return text.Length == 0 ? (title ?? "").Trim() : text;
        }

        // Search terms to try, best first: YouTube's own song and singer, then the cleaned title, then just the song name.
        internal static List<string> SearchTerms(YouTubeInfo yt, string title)
        {
            var terms = new List<string>();
            if (yt.Track.Length > 0) terms.Add(Plain(yt.Track + " " + yt.Artist.Split(',')[0]));
            string clean = CleanTitle(title);
            terms.Add(Plain(clean.Replace(" - ", " ")));
            int dash = clean.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0) terms.Add(Plain(clean.Substring(0, dash) + " " + clean.Substring(dash + 3)));
            if (dash > 0) terms.Add(Plain(clean.Substring(0, dash)));
            return terms.Where(t => t.Length > 1).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Without accents, so "Brahmāstra" also finds "Brahmastra".
        internal static string Plain(string text)
        {
            var sb = new StringBuilder();
            foreach (char c in (text ?? "").Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return Regex.Replace(sb.ToString().Normalize(NormalizationForm.FormC), @"\s+", " ").Trim();
        }

        // Fallback when no local model is set up: the catalogue song whose name is in the video title, with the
        // singer or album also mentioned somewhere in the video's own text.
        internal static CatalogMatch BestGuess(List<CatalogMatch> matches, YouTubeInfo yt, string title)
        {
            string haystack = Fold(title + " " + yt.Track + " " + yt.Artist + " " + yt.Album + " " + yt.Description);
            string titleText = Fold(title + " " + yt.Track);
            CatalogMatch best = null;
            int bestScore = 0;
            foreach (var m in matches)
            {
                int score = 0;
                string name = Fold(Regex.Replace(m.Title, @"\(.*?\)", ""));
                if (name.Length > 1 && titleText.Contains(name)) score += 50;
                foreach (var part in Regex.Split(m.Artist, @",|&| and ", RegexOptions.IgnoreCase))
                    if (Fold(part).Length > 2 && haystack.Contains(Fold(part))) { score += 30; break; }
                string album = Fold(Regex.Replace(m.Album, @"\(.*?\)|\[.*?\]", ""));
                if (album.Length > 2 && haystack.Contains(album)) score += 20;
                if (score > bestScore) { bestScore = score; best = m; }
            }
            return bestScore >= 70 ? best : null;
        }

        internal static string Fold(string text)
        {
            string decomposed = (text ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == UnicodeCategory.NonSpacingMark) continue;
                sb.Append(Char.IsLetterOrDigit(c) ? c : ' ');
            }
            return " " + Regex.Replace(sb.ToString(), @"\s+", " ").Trim() + " ";
        }

        internal static async Task<string> DownloadArtworkAsync(string url, CancellationToken cancel)
        {
            if (String.IsNullOrEmpty(url)) return null;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) })
            {
                var bytes = await http.GetByteArrayAsync(url);
                cancel.ThrowIfCancellationRequested();
                if (bytes.Length < 1000) return null;
                string file = Path.Combine(Path.GetTempPath(), "yc-art-" + Guid.NewGuid().ToString("N") + ".jpg");
                File.WriteAllBytes(file, bytes);
                return file;
            }
        }

        // Rewrites the MP3's tags (and cover art, when given) with FFmpeg, without re-encoding the audio.
        internal static void WriteTags(string encoder, string file, IList<KeyValuePair<string, string>> tags, string artwork)
        {
            string temp = Path.Combine(Path.GetDirectoryName(file), "." + Guid.NewGuid().ToString("N").Substring(0, 10) + ".tagging.mp3");
            var args = new List<string> { "-hide_banner", "-loglevel", "error", "-y", "-i", file };
            if (artwork != null) args.AddRange(new[] { "-i", artwork });
            args.AddRange(new[] { "-map", "0:a" });
            args.AddRange(artwork != null ? new[] { "-map", "1:v" } : new[] { "-map", "0:v?" });
            args.AddRange(new[] { "-c", "copy", "-id3v2_version", "3", "-write_id3v1", "1" });
            foreach (var tag in tags)
                if (!String.IsNullOrWhiteSpace(tag.Value)) args.AddRange(new[] { "-metadata", tag.Key + "=" + tag.Value.Trim() });
            if (artwork != null) args.AddRange(new[] { "-metadata:s:v", "title=Album cover", "-metadata:s:v", "comment=Cover (front)" });
            // yt-dlp's --embed-metadata leaves YouTube-only fields that Apple Music shows as junk ("escription"): blank them.
            foreach (var junk in new[] { "description", "synopsis", "purl" }) args.AddRange(new[] { "-metadata", junk + "=" });
            args.Add(temp);
            var info = new ProcessStartInfo(encoder, String.Join(" ", args.Select(Quote).ToArray()))
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8
            };
            try
            {
                using (var process = Process.Start(info))
                {
                    var errors = process.StandardError.ReadToEndAsync();
                    process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(120000)) { try { process.Kill(); } catch (InvalidOperationException) { } throw new TimeoutException("FFmpeg took too long to write the tags."); }
                    if (process.ExitCode != 0 || !File.Exists(temp) || new FileInfo(temp).Length < 1000)
                        throw new IOException("FFmpeg couldn't write the tags: " + errors.Result.Trim());
                }
                File.Copy(temp, file, true);
            }
            finally
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + Regex.Replace(value, "(\\\\*)(\"|$)", match =>
                new string('\\', match.Groups[1].Length * 2) + (match.Groups[2].Length == 0 ? "" : "\\\"")) + "\"";
        }
    }
}
