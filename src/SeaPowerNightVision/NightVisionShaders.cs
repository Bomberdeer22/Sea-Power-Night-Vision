using System;
using System.IO;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Locates the optional high-quality night vision shader.
    /// <para>
    /// Sea Power runs the built-in render pipeline with no post-processing package, so there is no
    /// stock grading stack to drive. Fixed-function blending can amplify and tint the image, but it
    /// cannot mix colour channels, which is what true monochrome-plus-phosphor requires. If a
    /// compiled shader is available this class finds it and the image effect switches to the
    /// full-quality path automatically.
    /// </para>
    /// <para>
    /// Two sources are tried: an asset bundle shipped next to the DLL
    /// (<c>nightvision.bundle</c>, built from <c>unity/NightVision.shader</c>), and any shader
    /// already loaded by the game that matches a known name.
    /// </para>
    /// </summary>
    internal static class NightVisionShaders
    {
        private const string ShaderName = "Hidden/SeaPower/NightVision";
        private const string BundleName = "nightvision.bundle";

        private static bool _searched;
        private static Shader _shader;

        /// <summary>The shader, or null when only the fixed-function path is available.</summary>
        public static Shader Get()
        {
            if (_searched)
            {
                return _shader;
            }

            _searched = true;

            _shader = Shader.Find(ShaderName);
            if (_shader != null)
            {
                NightVisionPlugin.Log.LogInfo($"Found '{ShaderName}' already loaded; using the full-quality filter.");
                return _shader;
            }

            _shader = LoadFromBundle();
            return _shader;
        }

        private static Shader LoadFromBundle()
        {
            try
            {
                var directory = Path.GetDirectoryName(typeof(NightVisionShaders).Assembly.Location);
                if (string.IsNullOrEmpty(directory))
                {
                    return null;
                }

                var path = Path.Combine(directory, BundleName);
                if (!File.Exists(path))
                {
                    return null;
                }

                var bundle = AssetBundle.LoadFromFile(path);
                if (bundle == null)
                {
                    NightVisionPlugin.Log.LogWarning($"'{path}' could not be loaded as an asset bundle.");
                    return null;
                }

                var shader = bundle.LoadAsset<Shader>(ShaderName);
                if (shader == null)
                {
                    foreach (var candidate in bundle.LoadAllAssets<Shader>())
                    {
                        shader = candidate;
                        break;
                    }
                }

                if (shader != null && shader.isSupported)
                {
                    NightVisionPlugin.Log.LogInfo($"Loaded the night vision shader from {BundleName}.");
                    return shader;
                }

                NightVisionPlugin.Log.LogWarning("The night vision shader bundle contained no usable shader.");
                return null;
            }
            catch (Exception e)
            {
                NightVisionPlugin.Log.LogWarning("Could not load the night vision shader bundle: " + e.Message);
                return null;
            }
        }
    }
}
