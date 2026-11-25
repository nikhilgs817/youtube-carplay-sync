using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YouTubeMp3;

class Workflow
{
    private static void Need(bool outcome, string message) { if (!outcome) throw new Exception(message); }
    private sealed class FixtureServer : HttpMessageHandler
    {
        private readonly bool ollama;
        public FixtureServer(bool value) { ollama = value; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            string list = ollama ? "/api/tags" : "/v1/models";
            if (request.RequestUri.AbsolutePath == list)
                return Reply(ollama ? "{\"models\":[{\"name\":\"local\"},{\"name\":\"foo:cloud\"}]}" : "{\"data\":[{\"id\":\"local\"}]}");
            Need(request.RequestUri.AbsolutePath == (ollama ? "/api/chat" : "/v1/chat/completions"), "Chat paths");
            Need(ollama ? request.Headers.Authorization == null : request.Headers.Authorization.Parameter == "key", "vLLM authentication");
            string body = await request.Content.ReadAsStringAsync();
            Need(body.Contains("Sample") && body.Contains(ollama ? "\"format\"" : "\"response_format\""), "Metadata and schema");
            return Reply(ollama ? "{\"message\":{\"content\":\"{\\\"genre\\\":\\\"pop\\\",\\\"language\\\":\\\"hindi\\\"}\"}}"
                : "{\"choices\":[{\"message\":{\"content\":\"{\\\"genre\\\":\\\"pop\\\",\\\"language\\\":\\\"hindi\\\"}\"}}]}");
        }
        private static HttpResponseMessage Reply(string value) { return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(value) }; }
    }

    private static void Shoot(Form form, string name)
    {
        using (var frame = new Bitmap(form.Width, form.Height))
        {
            form.DrawToBitmap(frame, new Rectangle(Point.Empty, frame.Size));
            frame.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name), ImageFormat.Png);
        }
    }

    [STAThread]
    static void Main()
    {
        var results = AudioDownload.ParseMatches("{\"entries\":[{\"id\":\"11111111111\",\"title\":\"Sample Song\",\"channel\":\"Sample Artist\",\"duration\":193},{\"id\":\"11111111111\"},{\"id\":\"invalid\"},null,{\"id\":\"22222222222\",\"uploader\":\"Second\"}]}");
        Need(results.Count == 2 && results[0].Duration == "3:13" && results[0].Link.EndsWith("11111111111") && results[1].Channel == "Second", "Search response");
        Need(VideoLink.Parse("https://youtu.be/11111111111\r\n\r\nhttps://youtube.com/watch?v=11111111111\r\ninvalid").Count == 2, "Notepad link deduplication");
        foreach (bool ollama in new[] { true, false })
        using (var client = new LocalModels(ollama ? LocalModels.Ollama : LocalModels.Vllm,
            "http://localhost:12345", "key", new FixtureServer(ollama)))
        {
            Need(client.ListAsync(CancellationToken.None).GetAwaiter().GetResult().SequenceEqual(new[] { "local" }), "Model picker");
            var suggestion = client.ClassifyAsync("local", new TrackMetadata { Title = "Sample" }, CancellationToken.None).GetAwaiter().GetResult();
            Need(MusicOrganizer.RelativeFolder(suggestion, MusicOrganizer.Both) == "Hindi-Pop", "AI suggestion");
        }

        string sandbox = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            string source = Path.Combine(sandbox, "source");
            string sign = Path.Combine(source, "Nested");
            Directory.CreateDirectory(sign);
            string song = Path.Combine(sign, "\u0938\u0902\u0917\u0940\u0924.mp3");
            var frame = new byte[417];
            frame[0] = 255; frame[1] = 251; frame[2] = 144; frame[3] = 192;
            using (var stream = File.Create(song))
                for (int i = 0; i < 45; i++) stream.Write(frame, 0, frame.Length);
            string untouched = Path.Combine(source, "other.mp3");
            File.Copy(song, untouched);
            var original = File.ReadAllBytes(song);

            var catalogDir = Path.Combine(sandbox, "catalog");
            var library = new MusicLibrary(source, catalogDir);
            Need(library.Tracks.Count == 2, "Recursive import");
            var songRow = library.Tracks.Single(t => t.Path == song);
            songRow.Title = "\u0938\u0902\u0917\u0940\u0924";
            songRow.Artist = "Sample Artist";
            songRow.Genre = "Pop";
            songRow.Language = "Hindi";
            songRow.WriteTitle = songRow.WriteArtist = true;
            library.Save();
            library = new MusicLibrary(source, catalogDir);
            songRow = library.Tracks.Single(t => t.Path == song);
            Need(songRow.Artist == "Sample Artist" && songRow.Group == "Hindi - Pop", "Persisted manual tags");
            string output = Path.Combine(sandbox, "export");
            var result = library.Export(output, new[] { songRow });
            Need(result.Count == 1 && result.Failures.Count == 0,
                "Tagged CarPlay export: " + String.Join(Environment.NewLine, result.Failures));
            string exported = Path.Combine(result.Directory, "Music", "Nested", Path.GetFileName(song));
            using (var tags = new Mp3Tags(exported, false))
                Need(tags.ReadTitle() == songRow.Title && tags.ReadArtist() == "Sample Artist" && tags.ReadGenre() == "Hindi - Pop", "Read tags back from the exported MP3");
            string rules = File.ReadAllText(Path.Combine(result.Directory, "Playlist-rules.txt"));
            Need(rules.Contains("Hindi Pop") && rules.Contains("Genre is Hindi - Pop"), "Smart Playlist rules");
            Need(!File.Exists(Path.Combine(result.Directory, "Music", "other.mp3")) && File.ReadAllBytes(song).SequenceEqual(original), "Selected music only and preserve originals");
            string organized = Path.Combine(sandbox, "organized");
            Need(MusicOrganizer.Copy(song, organized, "Hindi-Pop", CancellationToken.None), "Folder organization");
            Need(!MusicOrganizer.Copy(song, organized, "Hindi-Pop", CancellationToken.None), "Repeat skips duplicates");
            Console.WriteLine("PASS: MP3 tagged-copy export, Windows tag readback, selected tracks, playlist rules, originals, folders and persisted edits.");
        }
        finally { Directory.Delete(sandbox, true); }

        var setupPath = Path.GetFullPath(@"dist\YouTube-CarPlay-Sync-Setup.exe");
        var appPath = Path.GetFullPath(@"dist\YouTube-CarPlay-Sync\YouTube-CarPlay-Sync.exe");
        var setup = Assembly.LoadFile(setupPath);
        using (var appResource = setup.GetManifestResourceStream("app"))
        using (var destination = new MemoryStream())
        {
            appResource.CopyTo(destination);
            Need(destination.ToArray().SequenceEqual(File.ReadAllBytes(appPath)), "Verified app packaged in setup");
        }
        using (var icon = Icon.ExtractAssociatedIcon(appPath)) Need(icon != null, "App icon");
        using (var icon = Icon.ExtractAssociatedIcon(setupPath)) Need(icon != null, "Setup icon");
        using (var installer = (Form)Activator.CreateInstance(setup.GetType("YouTubeMp3.Setup.Installer")))
            Need(installer.Text == "YouTube CarPlay Sync Setup", "Setup title");
        // Song info lookup: title cleanup, catalogue parsing, matching and tag writing.
        Need(SongLookup.CleanTitle("Kesariya - Brahmāstra | Ranbir Kapoor, Alia Bhatt | Pritam | Arijit Singh | Amitabh Bhattacharya| 4K") == "Kesariya - Brahmāstra", "Clean title");
        Need(SongLookup.CleanTitle("\"Tum Hi Ho\" Aashiqui 2 Full Song With Lyrics | Aditya Roy Kapur") == "Tum Hi Ho Aashiqui 2", "Clean title with lyrics");
        Need(SongLookup.SearchTerms(new YouTubeInfo(), "Kesariya - Brahmāstra | X")[0] == "Kesariya Brahmastra", "Accent-free search term");
        var yt = YouTubeInfo.Parse("{\"title\":\"t\",\"channel\":\"Sony Music India\",\"track\":\"Kesariya\",\"artists\":[\"Arijit Singh\",\"Pritam\"],\"album\":\"Brahmastra\",\"release_year\":2022}");
        Need(yt.Artist == "Arijit Singh, Pritam" && yt.Year == "2022" && yt.Album == "Brahmastra", "YouTube music details");
        Need(SongLookup.SearchTerms(yt, "whatever")[0] == "Kesariya Arijit Singh", "Search with YouTube's song and singer first");
        var apple = SongLookup.ParseApple("{\"results\":[{\"trackName\":\"Kesariya (From \\\"Brahmastra\\\")\",\"artistName\":\"Pritam, Arijit Singh & Amitabh Bhattacharya\"," +
            "\"collectionName\":\"Kesariya (From \\\"Brahmastra\\\") - Single\",\"releaseDate\":\"2022-07-17T07:00:00Z\",\"primaryGenreName\":\"Bollywood\"," +
            "\"trackNumber\":1,\"trackCount\":1,\"artworkUrl100\":\"https://x/100x100bb.jpg\"},{\"trackName\":\"Other Song\",\"artistName\":\"Someone\",\"collectionName\":\"Else\"}]}");
        Need(apple.Count == 2 && apple[0].Year == "2022" && apple[0].Artwork.EndsWith("1000x1000bb.jpg") && apple[0].Track == 1, "Apple catalogue parsing");
        var guess = SongLookup.BestGuess(apple, new YouTubeInfo(), "Kesariya - Brahmāstra | Ranbir Kapoor, Alia Bhatt | Pritam | Arijit Singh");
        Need(guess != null && guess.Artist.StartsWith("Pritam"), "Pick the matching catalogue song");
        Need(SongLookup.BestGuess(apple, new YouTubeInfo(), "Completely Different Track") == null, "No match when nothing fits");
        var brainz = SongLookup.ParseMusicBrainz("{\"recordings\":[{\"title\":\"Tum Hi Ho\",\"first-release-date\":\"2013-04-01\",\"artist-credit\":[{\"name\":\"Arijit Singh\",\"joinphrase\":\" & \"},{\"name\":\"Mithoon\"}],\"releases\":[{\"id\":\"abc\",\"title\":\"Aashiqui 2\"}]}]}");
        Need(brainz.Count == 1 && brainz[0].Artist == "Arijit Singh & Mithoon" && brainz[0].Album == "Aashiqui 2" && brainz[0].Year == "2013" && brainz[0].Source == "MusicBrainz", "MusicBrainz parsing");
        var encoder = new DownloadTools().Encoder;
        if (encoder != null)
        {
            string tagged = Path.Combine(Path.GetTempPath(), "yc-tag-" + Guid.NewGuid().ToString("N") + ".mp3");
            try
            {
                var frame = new byte[417];
                frame[0] = 255; frame[1] = 251; frame[2] = 144; frame[3] = 192;
                using (var stream = File.Create(tagged)) for (int i = 0; i < 45; i++) stream.Write(frame, 0, frame.Length);
                string junked = tagged + ".junk.mp3";
                var junk = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(encoder,
                    "-hide_banner -loglevel error -y -i \"" + tagged + "\" -c copy -id3v2_version 3 -metadata description=escription -metadata purl=url \"" + junked + "\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                junk.WaitForExit();
                File.Copy(junked, tagged, true); File.Delete(junked);
                Need(System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(tagged)).Contains("escription"), "Junk YouTube tag planted for the test");
                SongLookup.WriteTags(encoder, tagged, new List<KeyValuePair<string, string>> {
                    new KeyValuePair<string, string>("title", "तुम ही हो"),
                    new KeyValuePair<string, string>("artist", "Arijit Singh"), new KeyValuePair<string, string>("album", "Aashiqui 2"),
                    new KeyValuePair<string, string>("genre", "Hindi - Romantic"), new KeyValuePair<string, string>("date", "2013") }, null);
                using (var tags = new Mp3Tags(tagged, false))
                    Need(tags.ReadTitle() == "तुम ही हो" && tags.ReadArtist() == "Arijit Singh" && tags.ReadGenre() == "Hindi - Romantic", "FFmpeg tag write with a Hindi title");
                Need(!System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(tagged)).Contains("escription"), "YouTube description/purl tags removed");
            }
            finally { try { File.Delete(tagged); } catch (IOException) { } }
        }
        var split = new SongJob { Language = "Hindi", Genre = "Hindi - Romantic" };
        split.SplitGroup();
        Need(split.Language == "Hindi" && split.Genre == "Romantic" && split.Group == "Hindi - Romantic", "Re-sent genre tag split back into language and genre");
        Console.WriteLine("PASS: song info lookup: title cleanup, Apple and MusicBrainz parsing, matching and FFmpeg tag writing.");

        Application.EnableVisualStyles();
        string store = Path.Combine(Path.GetTempPath(), "yc-qa-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var pipeline = new Pipeline(store, false);
            Func<string, Stage, string, string, string, SongJob> make = (title, stage, message, language, genre) =>
                new SongJob { Title = title, Stage = stage, Message = message, Language = language, Genre = genre, VideoId = "11111111111",
                    Link = "https://youtu.be/11111111111", Artist = "Sample Artist" };
            var downloading = make("Kesariya", Stage.Download, "Downloading \u00B7 46%", "", "");
            downloading.Percent = 46;
            var failed = make("Broken link", Stage.Failed, "This video is unavailable.", "", "");
            failed.FailedAt = Stage.Download; failed.Error = "This video is unavailable.";
            var review = make("\u0924\u0941\u092E \u0939\u0940 \u0939\u094B", Stage.Review, "AI suggested tags: check them", "Hindi", "Romantic");
            review.AiTagged = true;
            pipeline.Jobs.AddRange(new[] { downloading, make("Pal Pal", Stage.Tagging, "AI is reading the title", "", ""),
                review, make("Just a Boy", Stage.Done, "In Apple Music \u00B7 Japanese - Hip-Hop", "Japanese", "Hip-Hop"),
                make("Channa Mereya", Stage.Queued, "Waiting to start", "", ""), failed });
            Need(SongJob.GroupOf("Hindi", "Pop") == "Hindi - Pop" && SongJob.GroupOf("", "Pop") == "Pop" && SongJob.GroupOf(" ", "") == null, "Genre group");
            Need(downloading.Step == 0 && review.Step == 4 && pipeline.Jobs[3].Step == 5 && failed.Step == 0, "Step index");
            pipeline.Save();
            var reloaded = new Pipeline(store, false);
            Need(reloaded.Jobs.Count == 6 && reloaded.Jobs[2].Language == "Hindi" && reloaded.Jobs[0].Stage == Stage.Queued &&
                 reloaded.Jobs[2].Stage == Stage.Review, "Songs saved and resumed after a restart");
            using (var window = new MainWindow(pipeline))
            {
                Need(window.Text == "YouTube CarPlay Sync", "App branding");
                window.Show();
                for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(40); }
                Shoot(window, "MainWindow.png");
                using (var dialog = new ReviewDialog(pipeline.WaitingForReview, pipeline.Jobs))
                {
                    dialog.Show(window);
                    for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(30); }
                    Shoot(dialog, "ReviewDialog.png");
                    dialog.Close();
                }
                using (var settings = new SettingsWindow())
                {
                    settings.Show(window);
                    for (int i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(30); }
                    Shoot(settings, "SettingsWindow.png");
                    settings.Close();
                }
                window.Close();
            }
        }
        finally { try { File.Delete(store); } catch (IOException) { } }
        Console.WriteLine("PASS: search parsing, Notepad batch, Ollama and vLLM fixture requests, installer payload, saved song states, main screen, review and settings.");
    }
}
