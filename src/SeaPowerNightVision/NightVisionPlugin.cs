using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace SeaPowerNightVision
{
    /// <summary>
    /// BepInEx entry point. Creates the persistent controller object that owns the
    /// night vision state, hotkeys and rendering.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class NightVisionPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "io.github.bomberdeer22.seapower.nightvision";
        public const string PluginName = "Sea Power Night Vision";
        public const string PluginVersion = "1.3.0";

        internal static ManualLogSource Log { get; private set; }
        internal static NightVisionSettings Settings { get; private set; }
        internal static NightVisionController Controller { get; private set; }

        private static GameObject _hostObject;

        private void Awake()
        {
            Log = Logger;
            Settings = new NightVisionSettings(Config);
            Bootstrap();
            Log.LogInfo($"{PluginName} {PluginVersion} loaded. Toggle with {Settings.ToggleKey.Value}.");
            LogDiagnostics();
        }

        private void OnDestroy()
        {
            if (_hostObject != null)
            {
                Destroy(_hostObject);
                _hostObject = null;
            }
        }

        /// <summary>
        /// Dumps the handful of facts needed to diagnose "the mod does nothing" reports.
        /// </summary>
        private static void LogDiagnostics()
        {
            try
            {
                var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
                Log.LogInfo($"Unity {Application.unityVersion} | render pipeline: " +
                            (pipeline == null ? "Built-in" : pipeline.GetType().Name) +
                            $" | input backend: {InputBridge.BackendName}");
                Log.LogInfo($"Hotkeys -> toggle {Settings.ToggleKey.Value}, cycle {Settings.CycleModeKey.Value}, " +
                            $"gain {Settings.GainUpKey.Value}/{Settings.GainDownKey.Value}");
            }
            catch (System.Exception e)
            {
                Log.LogWarning("Diagnostics failed: " + e);
            }
        }

        /// <summary>
        /// Creates the controller. Safe to call more than once (e.g. when the mod is
        /// also chain-loaded through Anchor Chain).
        /// </summary>
        internal static void Bootstrap()
        {
            if (Controller != null)
            {
                return;
            }

            // When the mod is chain-loaded (Anchor Chain) rather than started as a BepInEx
            // plugin, Awake() never ran, so create the logger and config ourselves.
            if (Log == null)
            {
                Log = BepInEx.Logging.Logger.CreateLogSource(PluginName);
            }

            if (Settings == null)
            {
                var path = System.IO.Path.Combine(Paths.ConfigPath, PluginGuid + ".cfg");
                Settings = new NightVisionSettings(new ConfigFile(path, true));
            }

            _hostObject = new GameObject("SeaPowerNightVision");
            UnityEngine.Object.DontDestroyOnLoad(_hostObject);
            Controller = _hostObject.AddComponent<NightVisionController>();
        }
    }
}
