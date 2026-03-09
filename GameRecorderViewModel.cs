
using System.ComponentModel;
using RiskGameRecorder.Recording;

namespace RiskGameRecorder;

public sealed class GameRecorderViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Notify() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(""));

    public string RecorderStatus     { get; private set; } = "Not recording";
    public string TimeDisplay        { get; private set; } = "—";
    public string Round              { get; private set; } = "—";
    public string CurrentPlayer      { get; private set; } = "—";
    public string GameId             { get; private set; } = "—";

    public string Map                { get; private set; } = "—";
    public string GameMode           { get; private set; } = "—";
    public string Alliances          { get; private set; } = "—";
    public string Fog                { get; private set; } = "—";
    public string Blizzards          { get; private set; } = "—";
    public string Portals            { get; private set; } = "—";
    public string CardType           { get; private set; } = "—";
    public string Dice               { get; private set; } = "—";
    public string InactivityBehavior { get; private set; } = "—";

    public void Update(string recorderStatus, string timeDisplay, int? round,
                       string currentPlayerName, string gameId, GameConfig? config)
    {
        RecorderStatus     = recorderStatus;
        TimeDisplay        = timeDisplay;
        Round              = round.HasValue ? round.Value.ToString() : "—";
        CurrentPlayer      = !string.IsNullOrEmpty(currentPlayerName) ? currentPlayerName : "—";
        GameId             = IsValid(gameId) ? gameId : "—";
        Map                = config != null && IsValid(config.MapName)             ? config.MapName             : "—";
        GameMode           = config != null && IsValid(config.GameMode)            ? config.GameMode            : "—";
        Alliances          = config != null ? (config.Alliances  ? "On" : "Off") : "—";
        Fog                = config != null ? (config.FogOfWar   ? "On" : "Off") : "—";
        Blizzards          = config != null ? (config.Blizzards  ? "On" : "Off") : "—";
        Portals            = config != null && IsValid(config.Portals)             ? config.Portals             : "—";
        CardType           = config != null && IsValid(config.CardType)            ? config.CardType            : "—";
        Dice               = config != null && IsValid(config.Dice)                ? config.Dice                : "—";
        InactivityBehavior = config != null && IsValid(config.InactivityBehavior)  ? config.InactivityBehavior  : "—";
        Notify();
    }

    static bool IsValid(string s) =>
        !string.IsNullOrEmpty(s) && s != "(empty)" && s != "(invalid)" && s != "unknown";
}
