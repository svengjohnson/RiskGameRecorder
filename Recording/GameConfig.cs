namespace RiskGameRecorder.Recording;

public sealed record GameConfig(
    string GameId,
    string MapName,
    string GameMode,
    string CardType,
    string Dice,
    string InactivityBehavior,
    string Portals,
    bool   FogOfWar,
    bool   Blizzards,
    bool   Alliances
);
