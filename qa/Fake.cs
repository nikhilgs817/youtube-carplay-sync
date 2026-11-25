using System;
using System.IO;
using System.Text;
using System.Threading;
class Fake {
  static int Main(string[] args) {
    Console.OutputEncoding = new UTF8Encoding(false);
    var stage = args[Array.IndexOf(args, "-P") + 1];
    string uri = args[args.Length - 1];
    if (!uri.StartsWith("https://www.youtube.com/watch?v=") ||
        Array.IndexOf(args, "--no-playlist") < 0 || Array.IndexOf(args, "mp3") < 0) return 5;
    if (uri.EndsWith("22222222222")) { Console.Error.WriteLine("ERROR: video unavailable"); return 1; }
    if (uri.EndsWith("33333333333")) { File.WriteAllText(Path.Combine(stage, "incomplete.part"), "x"); Thread.Sleep(15000); }
    Console.WriteLine("MP3_PROGRESS: 34.0%");
    Console.WriteLine("MP3_PROGRESS:100.0%");
    var path = Path.Combine(stage, "S\u00f3ng & Title [11111111111].mp3");
    if (!uri.EndsWith("44444444444")) File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
    Console.WriteLine("MP3_FILE:" + path);
    return 0;
  }
}
