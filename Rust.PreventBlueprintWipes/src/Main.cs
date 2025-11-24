using HarmonyLib;

namespace Rust.PreventBlueprintWipes;

public class Main
{
    public const string HarmonyId = "com.rhianmaryland.preventblueprintwipes";

    public static void Load()
    {
        var harmony = new Harmony(HarmonyId);
        harmony.PatchAll();
    }
}
