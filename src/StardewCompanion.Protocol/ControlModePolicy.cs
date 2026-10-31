namespace StardewCompanion.Protocol;

public static class ControlModePolicy
{
    public static bool IsSoloTestRunId(string id)
    {
        if(!id.StartsWith("SC",StringComparison.Ordinal))return false;
        var marker=id.IndexOf("D_",StringComparison.Ordinal);
        return marker>2&&id.Length>marker+2&&int.TryParse(id.Substring(2,marker-2),out var days)&&days is >=1 and <=112;
    }
    public static bool CanControl(GameObservation state) => state.WorldReady && (!state.IsHost ||
        (state.ControlMode=="solo_test" && !state.Multiplayer && state.PlayerName=="SC5Host"
         && IsSoloTestRunId(state.TestRunId)));
}
