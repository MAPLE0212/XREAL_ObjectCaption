using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace XrealCaption.EditorTools
{
    /// <summary>
    /// Beam Pro 向けの APK を Builds/XrealCaption.apk に作る（メニュー：XREAL Caption → Build Android APK）。
    /// バッチモード：Unity -batchmode -quit -buildTarget Android -projectPath . -executeMethod XrealCaption.EditorTools.AndroidBuild.Run
    /// </summary>
    public static class AndroidBuild
    {
        const string k_OutputPath = "Builds/XrealCaption.apk";

        [MenuItem("XREAL Caption/Build Android APK")]
        public static void Run()
        {
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                Debug.LogError("[AndroidBuild] ビルドターゲットが Android ではありません。File → Build Profiles で Android に切り替えてから実行してください。");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }

            // Android でしか存在しない XREAL の設定（入力ソースなど）も含めて設定し直す
            ProjectSetup.Run();

            Directory.CreateDirectory(Path.GetDirectoryName(k_OutputPath));
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Main.unity" },
                locationPathName = k_OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            Debug.Log($"[AndroidBuild] {summary.result}: {summary.outputPath} ({summary.totalSize / (1024 * 1024)} MB, errors={summary.totalErrors})");
            if (Application.isBatchMode)
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
