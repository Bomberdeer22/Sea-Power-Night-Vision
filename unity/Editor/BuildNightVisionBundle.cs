using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor helper that packs NightVision.shader into nightvision.bundle.
/// Put this file in Assets/Editor/ of a Unity 6000.0.x project, then use
/// "Sea Power Night Vision > Build Shader Bundle" from the menu bar.
/// </summary>
public static class BuildNightVisionBundle
{
    private const string OutputDirectory = "BuiltBundles";
    private const string BundleName = "nightvision.bundle";
    private const string ShaderAsset = "Assets/NightVision.shader";

    [MenuItem("Sea Power Night Vision/Build Shader Bundle")]
    public static void Build()
    {
        if (!File.Exists(ShaderAsset))
        {
            EditorUtility.DisplayDialog(
                "Shader missing",
                $"Expected the shader at {ShaderAsset}.\n\nCopy NightVision.shader into the project's Assets folder first.",
                "OK");
            return;
        }

        Directory.CreateDirectory(OutputDirectory);

        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { ShaderAsset }
        };

        // StandaloneWindows64 matches the game; uncompressed loads fastest at startup.
        var manifest = BuildPipeline.BuildAssetBundles(
            OutputDirectory,
            new[] { build },
            BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        if (manifest == null)
        {
            Debug.LogError("Asset bundle build failed; see the console above.");
            return;
        }

        var path = Path.GetFullPath(Path.Combine(OutputDirectory, BundleName));
        Debug.Log($"Built {path}");
        EditorUtility.RevealInFinder(path);
    }
}
