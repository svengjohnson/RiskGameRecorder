
using System.Collections.Generic;

namespace RiskGameRecorder.Models;

public sealed class PlayerModel
{
    public string Name { get; init; } = "";
    public ulong UserId { get; init; }
    public string DeviceId { get; init; } = "";
    public string ColorId { get; init; } = "";
    public string SkillLevel { get; init; } = "";
    public string SkillLevel1v1 { get; init; } = "";
    public int BattlePoints { get; init; }
    public int LobbyIndex { get; set; }
    public bool IsQuit { get; set; }
    public bool IsBottedOut { get; set; }
    public bool IsAI { get; set; }
    public bool IsCurrentlyAutomated { get; set; }
    public int? Units { get; set; }
    public int? CapitalUnits { get; set; }
    public int? TerritoryCount { get; set; }
    public int? CapitalCount { get; set; }
    public List<TerritoryInfo>?  Territories { get; set; }
    public List<ContinentInfo>? Continents  { get; set; }
    public List<TerritoryCard> TerritoryCards { get; } = new();
    public List<string> AllyNames { get; set; } = new();
}
