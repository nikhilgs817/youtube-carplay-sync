// Checks for the 1.4 features that run on Linux with Mono, compiled together with the app's sources:
//   mcs -langversion:5 -r:... -main:FeatureTests -out:features.exe $(ls *.cs | grep -v Installer.cs) qa/FeatureTests.cs
//   mono features.exe <scratch folder>
// Needs ffmpeg and ffprobe on PATH for the Comments tag check. Makes no network calls: the AI providers answer
// through a fake HTTP handler that also records each request.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using YouTubeMp3;

static class FeatureTests
{
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name); if (!ok) failures++; }

    static int Main(string[] args)
    {
        string scratch = args.Length > 0 ? args[0] : Path.GetTempPath();
        Directory.CreateDirectory(scratch);
        Rules();
        Comments(scratch);
        Providers();
        PreferencesMigration();
        Review();
        Console.WriteLine(failures == 0 ? "All feature checks passed." : failures + " feature check(s) failed.");
        return failures == 0 ? 0 : 1;
    }

    // ---- no-model tagging -----------------------------------------------------------------------------------------

    static void Guess(string title, string channel, string description, CatalogMatch match, out string language, out string genre)
    {
        RuleTagger.Guess(new YouTubeInfo { Title = title, Channel = channel ?? "", Description = description ?? "" }, title, match,
            out language, out genre);
    }

    static void Rules()
    {
        string l, g;
        Guess("केसरिया तेरा इश्क़ है पिया", "", "", null, out l, out g);
        Check(l == "Hindi", "Devanagari title is Hindi (" + l + ")");
        Guess("ਮੈਂ ਤੇਰਾ ਹੋ ਗਿਆ", "", "", null, out l, out g);
        Check(l == "Punjabi", "Gurmukhi title is Punjabi, not Hindi (" + l + ")");
        Guess("Naatu Naatu | RRR", "", "", null, out l, out g);
        Check(l == "", "No evidence gives no language rather than a wrong one (" + l + ")");
        Guess("Kesariya | Brahmastra | Full Video Song", "T-Series", "", null, out l, out g);
        Check(l == "Hindi", "T-Series channel is Hindi (" + l + ")");
        Check(g == "Soundtrack", "Hindi film video song is Soundtrack (" + g + ")");
        Guess("Brown Munde", "Speed Records", "", null, out l, out g);
        Check(l == "Punjabi" && g == "Pop", "Speed Records is Punjabi, Pop (" + l + ", " + g + ")");
        Guess("Shree Hanuman Chalisa", "", "", null, out l, out g);
        Check(g == "Devotional", "Chalisa is Devotional (" + g + ")");
        Guess("Tum Hi Ho (Lofi Slowed + Reverb)", "", "", null, out l, out g);
        Check(l == "Hindi" && g == "Lo-fi", "Romanised Hindi with slowed + reverb (" + l + ", " + g + ")");
        Guess("Blinding Lights", "The Weeknd", "", new CatalogMatch { Title = "Blinding Lights", Artist = "The Weeknd", Genre = "R&B/Soul" }, out l, out g);
        Check(g == "R&B", "Apple's R&B/Soul maps to R&B (" + g + ")");
        Guess("Some Song", "", "", new CatalogMatch { Title = "Some Song", Genre = "Bollywood" }, out l, out g);
        Check(l == "Hindi" && g == "Soundtrack", "Apple's Bollywood genre gives Hindi, Soundtrack (" + l + ", " + g + ")");
        Guess("Dynamite", "HYBE LABELS", "Official MV k-pop", null, out l, out g);
        Check(l == "Korean", "K-pop in the description is Korean (" + l + ")");
        Check(RuleTagger.ByScript("사랑해") == "Korean" && RuleTagger.ByScript("ありがとう") == "Japanese", "Hangul and kana scripts");
    }

    // ---- playlist name in the Comments tag ------------------------------------------------------------------------

    static string Run(string file, string arguments)
    {
        var info = new System.Diagnostics.ProcessStartInfo(file, arguments) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        using (var p = System.Diagnostics.Process.Start(info))
        {
            string output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return output;
        }
    }

    static void Comments(string scratch)
    {
        string mp3 = Path.Combine(scratch, "comment-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".mp3");
        Run("ffmpeg", "-hide_banner -loglevel error -y -f lavfi -i sine=d=1 -metadata title=Kesariya -metadata comment=https://youtu.be/x -id3v2_version 3 \"" + mp3 + "\"");
        if (!File.Exists(mp3)) { Check(false, "ffmpeg made a test MP3"); return; }
        SongLookup.WriteTags("ffmpeg", mp3, new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("artist", "Arijit Singh") }, null);
        Check(Id3Comment.Set(mp3, "CarPlay Hindi"), "Comments tag written");
        string tags = Run("ffprobe", "-v error -show_entries format_tags -of default=nw=1 \"" + mp3 + "\"");
        Check(tags.Contains("TAG:comment=CarPlay Hindi"), "ffprobe reads the playlist from the Comments field");
        Check(!tags.Contains("youtu.be"), "The video link yt-dlp left in the comment is gone");
        Check(tags.Contains("TAG:title=Kesariya") && tags.Contains("TAG:artist=Arijit Singh"), "Other tags kept");
        byte[] bytes = File.ReadAllBytes(mp3);
        Check(IndexOf(bytes, Encoding.ASCII.GetBytes("COMM")) > 0 && IndexOf(bytes, Encoding.ASCII.GetBytes("TXXX")) < 0,
            "Stored as a COMM frame that Apple Music reads, not FFmpeg's TXXX");
        Check(Id3Comment.Set(mp3, "पंजाबी"), "Non-Latin playlist name written");
        tags = Run("ffprobe", "-v error -show_entries format_tags -of default=nw=1 \"" + mp3 + "\"");
        Check(tags.Contains("TAG:comment=पंजाबी"), "Non-Latin playlist name reads back");
        Check(Id3Comment.Set(mp3, ""), "Empty playlist clears the comment");
        tags = Run("ffprobe", "-v error -show_entries format_tags -of default=nw=1 \"" + mp3 + "\"");
        Check(!tags.Contains("TAG:comment="), "No comment left");
        string decoded = Run("ffmpeg", "-v error -i \"" + mp3 + "\" -f null -");
        Check(decoded.Trim().Length == 0, "Audio still decodes cleanly after the rewrite");
        byte[] bare = new byte[] { 0xFF, 0xFB, 0x90, 0x00, 1, 2, 3 };
        byte[] tagged = Id3Comment.Rewrite(bare, "CarPlay");
        Check(tagged != null && tagged[0] == 'I' && tagged.Skip(tagged.Length - bare.Length).SequenceEqual(bare), "A file without a tag gets one in front of the audio");
        File.Delete(mp3);
    }

    static int IndexOf(byte[] data, byte[] find)
    {
        for (int i = 0; i + find.Length <= data.Length; i++)
            if (!find.Where((b, j) => data[i + j] != b).Any()) return i;
        return -1;
    }

    // ---- AI providers ----------------------------------------------------------------------------------------------

    sealed class FakeApi : HttpMessageHandler
    {
        internal readonly List<HttpRequestMessage> Requests = new List<HttpRequestMessage>();
        internal readonly List<string> Bodies = new List<string>();
        internal Func<HttpRequestMessage, string, Tuple<HttpStatusCode, string>> Answer;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            string body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
            Requests.Add(request);
            Bodies.Add(body);
            var answer = Answer(request, body);
            return new HttpResponseMessage(answer.Item1) { Content = new StringContent(answer.Item2, Encoding.UTF8, "application/json") };
        }
    }

    static readonly object Schema = new
    {
        type = "object",
        properties = new { match = new { type = "integer" }, language = new { type = "string" }, genre = new { type = "string" } },
        required = new[] { "match", "language", "genre" },
        additionalProperties = false
    };

    static IDictionary<string, object> Ask(string provider, string model, FakeApi api)
    {
        using (var client = new CloudModels(provider, "sk-test-123", api) { ClassifyTimeout = 20 })
            return client.AskJsonAsync(model, "Identify the song.", new { title = "Kesariya" }, Schema, CancellationToken.None).Result;
    }

    static IDictionary<string, object> Json(string text) { return (IDictionary<string, object>)new JavaScriptSerializer().DeserializeObject(text); }

    static void Providers()
    {
        const string answer = "{\"match\":0,\"language\":\"Hindi\",\"genre\":\"Romantic\"}";

        var claude = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.OK,
            "{\"content\":[{\"type\":\"thinking\",\"thinking\":\"...\"},{\"type\":\"text\",\"text\":" + new JavaScriptSerializer().Serialize(answer) + "}],\"stop_reason\":\"end_turn\"}") };
        var result = Ask(AiProviders.Claude, "claude-opus-5-5", claude);
        var request = claude.Requests[0];
        var body = Json(claude.Bodies[0]);
        Check(Convert.ToString(result["language"]) == "Hindi", "Claude: answer read from the text block after thinking");
        Check(request.RequestUri.ToString() == "https://api.anthropic.com/v1/messages", "Claude: Messages API endpoint");
        Check(request.Headers.GetValues("x-api-key").Single() == "sk-test-123" && request.Headers.GetValues("anthropic-version").Single() == "2026-10-21",
            "Claude: key and API version headers");
        Check(request.Headers.GetValues("anthropic-beta").Single() == "server-side-fallback-2026-07-01" && Convert.ToString(body["fallbacks"]) == "default",
            "Claude Opus: refusal fallback turned on");
        var format = (IDictionary<string, object>)((IDictionary<string, object>)body["output_config"])["format"];
        Check(Convert.ToString(format["type"]) == "json_schema", "Claude: structured JSON output");
        Check(!body.ContainsKey("thinking") && !body.ContainsKey("temperature") && !body.ContainsKey("tool_choice"),
            "Claude: no thinking, temperature or forced tool settings");
        var haiku = new FakeApi { Answer = claude.Answer };
        Ask(AiProviders.Claude, "claude-haiku-5-5", haiku);
        Check(!haiku.Requests[0].Headers.Contains("anthropic-beta") && !Json(haiku.Bodies[0]).ContainsKey("fallbacks"), "Claude Haiku: no fallback beta");
        var refusal = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.OK, "{\"content\":[],\"stop_reason\":\"refusal\"}") };
        Check(Throws(() => Ask(AiProviders.Claude, "claude-sonnet-5-5", refusal), "declined"), "Claude: a refusal is reported, not parsed");

        var openai = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.OK,
            "{\"choices\":[{\"message\":{\"content\":" + new JavaScriptSerializer().Serialize(answer) + "}}]}") };
        result = Ask(AiProviders.ChatGpt, "gpt-5.5", openai);
        body = Json(openai.Bodies[0]);
        Check(Convert.ToString(result["genre"]) == "Romantic", "ChatGPT: answer parsed");
        Check(openai.Requests[0].RequestUri.ToString() == "https://api.openai.com/v1/chat/completions" &&
              openai.Requests[0].Headers.Authorization.ToString() == "Bearer sk-test-123", "ChatGPT: endpoint and bearer key");
        Check(Convert.ToString(((IDictionary<string, object>)body["response_format"])["type"]) == "json_schema" && !body.ContainsKey("temperature") &&
              !body.ContainsKey("max_tokens"), "ChatGPT: strict JSON schema, no temperature or max_tokens");

        int calls = 0;
        var older = new FakeApi { Answer = (r, b) => ++calls == 1 ? Tuple.Create(HttpStatusCode.BadRequest, "{\"error\":{\"message\":\"json_schema not supported\"}}")
            : Tuple.Create(HttpStatusCode.OK, "{\"choices\":[{\"message\":{\"content\":\"```json\\n" + answer.Replace("\"", "\\\"") + "\\n```\"}}]}") };
        result = Ask(AiProviders.Grok, "grok-4.3", older);
        Check(older.Requests[0].RequestUri.Host == "api.x.ai" && Convert.ToString(result["language"]) == "Hindi",
            "Grok: falls back to JSON mode on HTTP 400 and reads a fenced answer");
        Check(Convert.ToString(((IDictionary<string, object>)Json(older.Bodies[1])["response_format"])["type"]) == "json_object", "Grok: second try is json_object");

        var gemini = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.OK,
            "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":" + new JavaScriptSerializer().Serialize(answer) + "}]}}]}") };
        result = Ask(AiProviders.Gemini, "gemini-3.5-flash", gemini);
        Check(gemini.Requests[0].RequestUri.ToString() == "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:generateContent" &&
              gemini.Requests[0].Headers.GetValues("x-goog-api-key").Single() == "sk-test-123", "Gemini: endpoint and key header");
        Check(Convert.ToString(result["genre"]) == "Romantic" && gemini.Bodies[0].Contains("match (integer)"), "Gemini: JSON mode with the fields named");

        var denied = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.Unauthorized, "{\"error\":{\"message\":\"invalid key sk-test-123\"}}") };
        string message = "";
        try { Ask(AiProviders.ChatGpt, "gpt-5.5", denied); }
        catch (AggregateException ex) { message = ex.InnerException.Message; }
        Check(message.Contains("refused the API key") && !message.Contains("sk-test-123"), "A bad key is explained without echoing the key");

        var list = new FakeApi { Answer = (r, b) => Tuple.Create(HttpStatusCode.OK,
            "{\"data\":[{\"id\":\"gpt-5.5\"},{\"id\":\"whisper-1\"},{\"id\":\"gpt-5.4-mini\"},{\"id\":\"text-embedding-3-large\"},{\"id\":\"gpt-realtime\"}]}") };
        using (var client = new CloudModels(AiProviders.ChatGpt, "sk-test-123", list))
        {
            var names = client.ListAsync(CancellationToken.None).Result;
            Check(names.SequenceEqual(new[] { "gpt-5.5", "gpt-5.4-mini" }), "ChatGPT model list keeps chat models only (" + String.Join(", ", names) + ")");
        }
        Check(Throws(() => new CloudModels(AiProviders.Claude, " "), "API key"), "No key: asks for one in Settings");
    }

    static bool Throws(Action action, string expected)
    {
        try { action(); return false; }
        catch (Exception ex)
        {
            var inner = ex is AggregateException ? ((AggregateException)ex).Flatten().InnerException : ex;
            return inner.Message.IndexOf(expected, StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    // ---- settings from before 1.4 -----------------------------------------------------------------------------------

    static void PreferencesMigration()
    {
        var old = new JavaScriptSerializer().Deserialize<Preferences>("{\"Provider\":\"vLLM\",\"Endpoint\":\"http://gpu:8000/v1\",\"Model\":\"qwen3\",\"AiEnabled\":true}");
        Check(old.ActiveProvider == AiProviders.Vllm && old.ModelFor(AiProviders.Vllm) == "qwen3" && old.EndpointFor(AiProviders.Vllm) == "http://gpu:8000/v1",
            "1.3 settings: vLLM, its model and address carry over");
        Check(old.ModelFor(AiProviders.Claude) == "" && old.EndpointFor(AiProviders.Ollama) == "http://127.0.0.1:11434", "Other providers start empty");
        var off = new JavaScriptSerializer().Deserialize<Preferences>("{\"AiEnabled\":false}");
        Check(off.ActiveProvider == AiProviders.None && !off.UseAi, "1.3 settings with AI off become No AI");
        var fresh = new Preferences();
        Check(fresh.ActiveProvider == AiProviders.Ollama && AiProviders.Create(fresh) == null, "New install: Ollama chosen, nothing runs until a model is picked");
        fresh.AiProvider = AiProviders.Claude;
        fresh.SetModel(AiProviders.Claude, "claude-haiku-5-5");
        Check(AiProviders.Create(fresh) == null, "Claude without a key doesn't run");
        var saved = new JavaScriptSerializer().Serialize(fresh);
        var back = new JavaScriptSerializer().Deserialize<Preferences>(saved);
        Check(back.ActiveProvider == AiProviders.Claude && back.ModelFor(AiProviders.Claude) == "claude-haiku-5-5", "Provider and model survive a save");
        back.RememberPlaylist("CarPlay"); back.RememberPlaylist("Gym"); back.RememberPlaylist("carplay");
        Check(back.KnownPlaylists.SequenceEqual(new[] { "carplay", "Gym" }), "Playlist names remembered once, newest first");
        var job = new JavaScriptSerializer().Deserialize<SongJob>("{\"Title\":\"Kesariya\",\"NewGroup\":\"Hindi - Pop\"}");
        Check(job.Title == "Kesariya" && job.Playlist == "", "A 1.3 song list still loads");
    }

    static void Review()
    {
        Check(ReviewDialog.IsYear("2023") && !ReviewDialog.IsYear("23") && !ReviewDialog.IsYear("2O23") && !ReviewDialog.IsYear("3023"),
            "Year must be four digits and plausible");
        var apps = new SendCheck.Running { Music = false };
        Check(SendCheck.Stalled("/nowhere.mp3", TimeSpan.FromSeconds(30), apps).Status.StartsWith("Open Apple Music"), "Apple Music closed: open it");
        apps = new SendCheck.Running { Music = true, Devices = true };
        Check(SendCheck.Stalled("/nowhere.mp3", TimeSpan.FromSeconds(30), apps).Title.Contains("Apple Devices"), "Apple Devices open: named with the fix");
        apps = new SendCheck.Running { Music = true, ITunes = true };
        Check(SendCheck.Stalled("/nowhere.mp3", TimeSpan.FromSeconds(30), apps).Title.Contains("iTunes"), "iTunes open: named with the fix");
        apps = new SendCheck.Running { Music = true };
        Check(SendCheck.Stalled("/nowhere.mp3", TimeSpan.FromSeconds(30), apps).Title == null, "Just busy: no banner yet");
        Check(SendCheck.Stalled("/nowhere.mp3", TimeSpan.FromMinutes(4), apps).Fix.Contains("quit Apple Music fully"), "Stuck 4 minutes: hidden window explained");
        Check(SendCheck.BeforeSend(null).Blocks, "No import folder blocks sending");
    }
}
