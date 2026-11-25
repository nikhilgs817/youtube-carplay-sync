using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using YouTubeMp3;
class Smoke
{
    static void Check(bool result, string message) { if (!result) throw new Exception(message); }

    sealed class FakeServer : HttpMessageHandler
    {
        private readonly bool ollama;
        public FakeServer(bool local) { ollama = local; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Check(request.RequestUri.Host == "localhost", "Only the selected address");
            Check(ollama ? request.Headers.Authorization == null : request.Headers.Authorization.Parameter == "fixture", "Authentication scoping");
            string expected = ollama ? "/api/tags" : "/v1/models";
            if (request.RequestUri.AbsolutePath == expected)
            {
                string list = ollama ? "{\"models\":[{\"name\":\"qwen-local\"},{\"name\":\"remote:cloud\"},{\"name\":\"alias\",\"remote_host\":\"https://cloud.invalid\"}]}"
                                     : "{\"data\":[{\"id\":\"vllm-local\"}]}";
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(list) };
            }
            Check(request.RequestUri.AbsolutePath == (ollama ? "/api/chat" : "/v1/chat/completions"), "Chat route");
            var requestText = await request.Content.ReadAsStringAsync();
            Check(requestText.Contains(ollama ? "\"format\"" : "\"response_format\"") &&
                  requestText.Contains("Fixture Song") && requestText.Contains("\"stream\":false"), "Structured metadata-only request");
            const string category = "{\"content\":\"{\\\"genre\\\":\\\"pop\\\",\\\"language\\\":\\\"hindi\\\"}\"}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                 ollama ? "{\"message\":" + category + "}" : "{\"choices\":[{\"message\":" + category + "}]}") };
        }
    }

    [STAThread]
    static void Main()
    {
        foreach (bool useOllama in new[] { true, false })
        {
            using (var models = new LocalModels(useOllama ? LocalModels.Ollama : LocalModels.Vllm, "http://localhost:14580/", "fixture", new FakeServer(useOllama)))
            {
                var choices = models.ListAsync(CancellationToken.None).GetAwaiter().GetResult();
                Check(choices.SequenceEqual(new[] {useOllama ? "qwen-local" : "vllm-local"}), "Model loading and cloud filtering");
                var labels = models.ClassifyAsync(choices[0], new TrackMetadata { Filename = "Fixture Song" }, CancellationToken.None).GetAwaiter().GetResult();
                Check(MusicOrganizer.RelativeFolder(labels, MusicOrganizer.Both) == "Hindi-Pop", "Language/genre classification");
                Check(MusicOrganizer.RelativeFolder(labels, MusicOrganizer.Genre) == "Pop", "Genre");
                Check(MusicOrganizer.RelativeFolder(labels, MusicOrganizer.Language) == "Hindi", "Language");
            }
        }

        string sandbox = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Guid.NewGuid().ToString("N"));
        var source = Directory.CreateDirectory(Path.Combine(sandbox, "source")).FullName;
        var nested = Directory.CreateDirectory(Path.Combine(source, "another")).FullName;
        string output = Path.Combine(source, "organized");
        try
        {
            var one = Path.Combine(source, "Fixture Song.mp3"); var two = Path.Combine(nested, "Fixture Song.mp3");
            File.WriteAllText(one, "one"); File.WriteAllText(two, "two"); File.WriteAllText(Path.Combine(source, "ignored.txt"), "ignored");
            Check(MusicOrganizer.Scan(source, output, CancellationToken.None).Length == 2, "Recursive scan");
            Check(TrackMetadata.Read(one).Filename == "Fixture Song", "Missing metadata fallback");
            Check(MusicOrganizer.RelativeFolder(new Category("a/b", "con"), MusicOrganizer.Both) == "_Con-A B", "Windows folder sanitization");
            Check(MusicOrganizer.Copy(one, output, "Hindi-Pop", CancellationToken.None), "Copy metadata-based folder");
            Check(!MusicOrganizer.Copy(one, output, "Hindi-Pop", CancellationToken.None), "Idempotent repeat");
            Check(MusicOrganizer.Copy(two, output, "Hindi-Pop", CancellationToken.None), "Save conflicting filename");
            Check(File.Exists(one) && File.Exists(two) && File.Exists(Path.Combine(output, "Hindi-Pop", "Fixture Song (2).mp3")), "Preserve source and destination files");
            Check(MusicOrganizer.Scan(source, output, CancellationToken.None).Length == 2, "Skip organized output");
            using (var stop = new CancellationTokenSource())
            {
                stop.Cancel();
                try { MusicOrganizer.Copy(one, output, "Another", stop.Token); throw new Exception("Expected cancel"); }
                catch (OperationCanceledException) { }
            }
        }
        finally { Directory.Delete(sandbox, true); }

        var app = Path.GetFullPath(@"dist\YouTube-MP3\YouTube-MP3.exe");
        var setup = Path.GetFullPath(@"dist\YouTube-MP3-Setup.exe");
        using (Icon icon = Icon.ExtractAssociatedIcon(app)) Check(icon != null, "App icon resource");
        using (Icon icon = Icon.ExtractAssociatedIcon(setup)) Check(icon != null, "Installer icon resource");
        var assembly = Assembly.LoadFile(setup);
        using (var resource = assembly.GetManifestResourceStream("app"))
        using (var payload = new MemoryStream())
        {
            resource.CopyTo(payload);
            Check(File.ReadAllBytes(app).SequenceEqual(payload.ToArray()), "Installer contains the verified app EXE");
        }
        using (var appForm = new MainWindow())
        {
            appForm.CreateControl(); appForm.PerformLayout();
            Check(appForm.ClientSize.Width >= 900 && appForm.Controls.Count > 0, "Main form initialization");
        }
        using (var settings = new SettingsWindow())
        {
            settings.CreateControl(); settings.PerformLayout();
            Check(settings.Controls.OfType<SettingsPage>().Any(), "Settings window initialization");
        }
        using (var info = (Form)Activator.CreateInstance(typeof(InfoWindow), true))
        {
            info.CreateControl(); info.PerformLayout();
            Check(info.Controls.Count > 0, "Info page initialization");
        }
        using (var installer = (Form)Activator.CreateInstance(assembly.GetType("YouTubeMp3.Setup.Installer")))
        {
            installer.CreateControl();
            Check(installer.Controls.Count > 0, "Setup form initialization");
        }
        var linkBatch = VideoLink.Parse("https://youtu.be/11111111111\nhttps://youtube.com/watch?v=11111111111\ninvalid");
        Check(linkBatch.Count == 2 && linkBatch[0].Id != null && linkBatch[1].Id == null, "Previous download batch parsing");
        Console.WriteLine("PASS: provider paths and auth, discovery and cloud filtering, structured classification, all grouping modes, metadata fallback, safe copy and cancellation, both icons, installer payload, main window, Settings and the info page.");
    }
}
