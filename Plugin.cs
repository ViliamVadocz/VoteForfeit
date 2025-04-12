using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace VoteForfeit;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Puck.exe")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log;

    private readonly Harmony harmony = new Harmony(MyPluginInfo.PLUGIN_GUID);

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
        harmony.PatchAll();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} patched these methods:");
        foreach (var method in harmony.GetPatchedMethods())
        {
            if (method.DeclaringType != null) Log.LogInfo($" - {method.DeclaringType.FullName}.{method.Name}");
        }
    }
}
