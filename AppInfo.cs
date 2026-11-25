using System.Reflection;

[assembly: AssemblyProduct(YouTubeMp3.AppInfo.Name)]
[assembly: AssemblyVersion(YouTubeMp3.AppInfo.FileVersion)]
[assembly: AssemblyFileVersion(YouTubeMp3.AppInfo.FileVersion)]

namespace YouTubeMp3
{
    internal static class AppInfo
    {
        internal const string Name = "YouTube CarPlay Sync";
        internal const string Version = "1.3.0";
        internal const string FileVersion = Version + ".0";
    }
}
