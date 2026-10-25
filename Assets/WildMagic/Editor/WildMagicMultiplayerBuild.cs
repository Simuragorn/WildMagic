using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace WildMagic.Prototype.Editor
{
    public static class WildMagicMultiplayerBuild
    {
        private const string BuildRoot = "Builds";

        [MenuItem("Wild Magic/Build Multiplayer Test")]
        public static void BuildMultiplayerTest()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
                throw new InvalidOperationException("At least one enabled scene is required.");

            Directory.CreateDirectory(BuildRoot);
            BuildWindowsHost(scenes);
            BuildWebClient(scenes);
            CopyLauncherFiles();
            Debug.Log($"WildMagic multiplayer build is ready at {Path.GetFullPath(BuildRoot)}");
        }

        private static void BuildWindowsHost(string[] scenes)
        {
            string folder = Path.Combine(BuildRoot, "WindowsHost");
            Directory.CreateDirectory(folder);
            BuildReportGuard(BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(folder, "WildMagicHost.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.Development
            }), "Windows host");
        }

        private static void BuildWebClient(string[] scenes)
        {
            string folder = Path.Combine(BuildRoot, "WebGL");
            Directory.CreateDirectory(folder);
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            BuildReportGuard(BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = folder,
                target = BuildTarget.WebGL,
                options = BuildOptions.Development
            }), "WebGL client");
        }

        private static void CopyLauncherFiles()
        {
            Copy("Tools/Serve-WildMagicWebGL.ps1", Path.Combine(BuildRoot, "Serve-WildMagicWebGL.ps1"));
            Copy("Tools/Start-WildMagicHost.cmd", Path.Combine(BuildRoot, "Start-WildMagicHost.cmd"));
        }

        private static void Copy(string source, string destination)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException($"Missing multiplayer launcher file: {source}");
            File.Copy(source, destination, true);
        }

        private static void BuildReportGuard(UnityEditor.Build.Reporting.BuildReport report, string label)
        {
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                throw new InvalidOperationException($"{label} build failed: {report.summary.result}");
        }
    }
}
