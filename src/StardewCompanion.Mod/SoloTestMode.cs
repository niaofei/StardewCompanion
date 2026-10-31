using StardewModdingAPI;
using StardewValley;
using StardewCompanion.Protocol;

namespace StardewCompanion.Mod;

internal static class SoloTestMode
{
    internal static string RunId=>Environment.GetEnvironmentVariable("SC_SOLO_TEST_NAME")??"";
    internal static bool Enabled=>Environment.GetEnvironmentVariable("SC_SOLO_TEST")=="1"
        && ControlModePolicy.IsSoloTestRunId(RunId)&&Context.IsWorldReady&&Context.IsMainPlayer
        &&!Context.IsMultiplayer&&Game1.player.Name=="SC5Host"&&Game1.player.farmName.Value==RunId;
}
