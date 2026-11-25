// Automated checks that run on Linux with Mono against the compiled app (no YouTube access needed).
using System;
using System.IO;
using System.Linq;
using System.Threading;
using YouTubeMp3;

static class LinuxTests
{
    static int failures;
    static void Check(bool ok, string name) { Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name); if (!ok) failures++; }

    static int Main(string[] args)
    {
        string scratch = args[0];
        // URL parsing, including the handover's radio-mix link.
        var mix = new VideoLink("https://www.youtube.com/watch?v=Dhx2EIhYkc0&list=RDDhx2EIhYkc0&start_radio=1");
        Check(mix.Id == "Dhx2EIhYkc0", "Radio/playlist link resolves to the single video Dhx2EIhYkc0");
        Check(new VideoLink("https://youtu.be/Dhx2EIhYkc0?si=abc").Id == "Dhx2EIhYkc0", "youtu.be link with share id");
        Check(new VideoLink("https://music.youtube.com/watch?v=Dhx2EIhYkc0&feature=share").Id == "Dhx2EIhYkc0", "YouTube Music link");
        Check(new VideoLink("https://www.youtube.com/shorts/Dhx2EIhYkc0").Id == "Dhx2EIhYkc0", "Shorts link");
        Check(new VideoLink("https://evil.example/watch?v=Dhx2EIhYkc0").Id == null, "Other sites are rejected");
        Check(new VideoLink("Kesariya Arijit Singh").Id == null, "Plain text is not a link");
        var batch = VideoLink.Parse("https://youtu.be/Dhx2EIhYkc0\r\n\r\nhttps://www.youtube.com/watch?v=Dhx2EIhYkc0\r\nnot a link");
        Check(batch.Count == 2 && batch[1].Id == null, "Batch input: blank lines ignored, duplicate video once, invalid line kept for reporting");

        // Error explanations.
        Check(DownloadException.Explain("yt-dlp: error: no such option: --js-runtimes", "").Outdated, "Old yt-dlp is detected as outdated");
        Check(DownloadException.Explain("ERROR: [youtube] abc: Sign in to confirm you're not a bot", "").Message.Contains("not a bot"), "Bot check explained");
        Check(DownloadException.Explain("ERROR: [youtube] abc: Private video", "").Message == "This video is private.", "Private video explained");
        Check(DownloadException.Explain("ERROR: unable to download video data: HTTP Error 403: Forbidden", "").Outdated, "HTTP 403 triggers update-and-retry");
        Check(!DownloadException.Explain("ERROR: [youtube] abc: Video unavailable", "").Outdated, "Unavailable video does not trigger an update");

        // Real yt-dlp + FFmpeg conversion of a Hindi-titled source, with the console in cp1252 like Windows.
        string dest = Path.Combine(scratch, "dest-" + Guid.NewGuid().ToString("N").Substring(0, 6));
        Directory.CreateDirectory(dest);
        var tools = new DownloadTools(Path.Combine(scratch, "fake", "yt-dlp"), "/usr/bin/ffmpeg", null);
        int lastPercent = -5; string stages = "";
        var link = new VideoLink("https://www.youtube.com/watch?v=Dhx2EIhYkc0");
        string output = AudioDownload.Save(link, dest, tools, new DownloadOptions { Quality = "192k", Artwork = true },
            (percent, stage) => { if (percent >= 0) lastPercent = percent; if (!stages.Contains(stage)) stages += stage + "|"; }, CancellationToken.None);
        Check(output != null && File.Exists(output), "MP3 produced: " + Path.GetFileName(output));
        Check(Path.GetFileName(output).StartsWith("पल पल café"), "Hindi + accented title preserved in the file name");
        Check(new FileInfo(output).Length > 1000, "MP3 is not empty");
        Check(!Directory.EnumerateDirectories(dest).Any(), "Temporary work folder removed");
        Check(lastPercent >= 0, "Progress reported (last " + lastPercent + "%), stages: " + stages);
        string argsUsed = File.ReadAllText(Path.Combine(scratch, "fake", "last-args.txt"));
        Check(argsUsed.Contains("--encoding utf-8") && argsUsed.Contains("192K") && argsUsed.Contains("--embed-thumbnail"), "UTF-8 output, chosen quality and artwork requested");
        var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("ffprobe",
            "-v error -show_entries format=format_name:stream=codec_name -of csv=p=0 \"" + output + "\"") { UseShellExecute = false, RedirectStandardOutput = true });
        string fmt = probe.StandardOutput.ReadToEnd(); probe.WaitForExit();
        Check(fmt.Contains("mp3"), "ffprobe confirms MP3 audio (" + fmt.Replace("\n", " ").Trim() + ")");

        // Duplicate detection uses the video id in the name; a fake-id copy proves the lookup.
        File.Copy(output, Path.Combine(dest, "Song [Dhx2EIhYkc0].mp3"));
        Check(AudioDownload.Existing(link, dest) != null, "Already-downloaded video is detected and skipped");
        Check(AudioDownload.Existing(new VideoLink("https://youtu.be/AAAAAAAAAAA"), dest) == null, "A different video is not treated as a duplicate");

        // Interrupted work folders are cleaned, other folders are not.
        string stale = Path.Combine(dest, ".yc-stale12345");
        string keep = Path.Combine(dest, "My Album");
        Directory.CreateDirectory(stale); Directory.CreateDirectory(keep);
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddDays(-2));
        AudioDownload.CleanStaleWork(dest);
        Check(!Directory.Exists(stale) && Directory.Exists(keep), "Stale work folder removed, user folder kept");

        // Missing tools give a clear message, not a crash.
        try { AudioDownload.Save(link, dest, new DownloadTools(null, null, null), (p, s) => { }, CancellationToken.None); Check(false, "Missing tools rejected"); }
        catch (DownloadException ex) { Check(ex.Message.Contains("not installed") && ex.Hint.Contains("Install tools"), "Missing tools: " + ex.Message + " " + ex.Hint); }

        // Cancellation stops the converter and cleans up.
        var cts = new CancellationTokenSource(150);
        try { AudioDownload.Save(link, dest, tools, (p, s) => { }, cts.Token); Check(false, "Cancel"); }
        catch (OperationCanceledException) { Check(!Directory.EnumerateDirectories(dest, ".yc-*").Any(), "Cancel stops the job and removes its work folder"); }

        Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
        return failures;
    }
}
