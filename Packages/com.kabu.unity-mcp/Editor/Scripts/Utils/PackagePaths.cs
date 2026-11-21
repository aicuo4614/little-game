using System.IO;
using UnityEngine;

namespace KabuUnityMcp.Editor.Utils
{
    internal static class PackagePaths
    {
        public static string GetPackageRoot()
        {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(PackagePaths).Assembly);
            if (info != null && !string.IsNullOrEmpty(info.resolvedPath))
                return info.resolvedPath;

            var projectRoot = Path.GetDirectoryName(Application.dataPath);
            var candidate = Path.GetFullPath(Path.Combine(projectRoot, ".."));
            if (File.Exists(Path.Combine(candidate, "package.json")))
                return candidate;

            return projectRoot;
        }

        public static string GetServerProjectPath()
        {
            return Path.Combine(GetPackageRoot(), "Server~", "kabu-unity-mcp.csproj");
        }

        public static string GetServerDirectory()
        {
            return Path.Combine(GetPackageRoot(), "Server~");
        }
    }
}
