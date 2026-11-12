using BepInEx;

namespace UL2Airtrike
{
    // Carries the UL-2 Airtrike bundle (embedded .nobp, found and loaded by Blueprinter). No game patches yet.
    [BepInPlugin("nick.nuclearoption.ul2airtrike", "UL-2 Airtrike TEST BUILD", "0.3.0")]
    [BepInDependency("com.nikkorap.blueprinter", BepInDependency.DependencyFlags.SoftDependency)]
    public class Plugin : BaseUnityPlugin
    {
        private void Awake() { Logger.LogInfo("UL-2 Airtrike TEST BUILD 0.3.0 loaded"); }
    }
}
