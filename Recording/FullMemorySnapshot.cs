using System.Collections.Generic;
using RiskGameRecorder.Memory;
using RiskGameRecorder.Models;

namespace RiskGameRecorder.Recording;

public sealed class FullMemorySnapshot
{
    public GameState           State             { get; init; }
    public List<PlayerModel>   Players           { get; init; } = new();
    public int                 Round             { get; init; }
    public string              CurrentPlayerName { get; init; } = "";
    public string              CurrentPlayerId   { get; init; } = "";
    public string              GameId            { get; init; } = "";
    public List<TerritorySnapshot> MapState      { get; init; } = new();
}
