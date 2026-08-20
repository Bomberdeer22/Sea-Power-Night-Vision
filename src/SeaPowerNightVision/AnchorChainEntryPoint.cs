#if ANCHORCHAIN
using AnchorChain;

namespace SeaPowerNightVision
{
    /// <summary>
    /// Optional entry point so the mod can also be distributed through the Steam Workshop
    /// via the community chainloader, Anchor Chain.
    /// <para>
    /// Only compiled when the project is built with <c>/p:AnchorChain=true</c>, so the plain
    /// BepInEx build has no dependency on AnchorChain.dll.
    /// </para>
    /// </summary>
    [ACPlugin(
        "io.github.bomberdeer22.seapower.nightvision",
        "Sea Power Night Vision",
        NightVisionPlugin.PluginVersion)]
    public class AnchorChainEntryPoint : IAnchorChainMod
    {
        public void TriggerEntryPoint()
        {
            // When loaded through Anchor Chain the BepInEx plugin attribute is not used, so
            // create the controller ourselves. Bootstrap() is idempotent.
            NightVisionPlugin.Bootstrap();
        }
    }
}
#endif
