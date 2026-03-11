using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RiskGameRecorder.Recording;

public sealed class RecordedGame
{
    [JsonPropertyName("metadata")]
    public RecordedMetadata Metadata { get; set; } = new();

    [JsonPropertyName("gameInfo")]
    public RecordedGameInfo GameInfo { get; set; } = new();

    [JsonPropertyName("players")]
    public Dictionary<string, RecordedPlayer> Players { get; set; } = new();

    [JsonPropertyName("continents")]
    public Dictionary<string, int> Continents { get; set; } = new();

    [JsonPropertyName("blizzards")]
    public List<string> Blizzards { get; set; } = new();

    [JsonPropertyName("roundInfo")]
    public Dictionary<string, RecordedRound> RoundInfo { get; set; } = new();
}

public sealed class RecordedMetadata
{
    [JsonPropertyName("version")] public int    Version { get; set; } = 1;
    [JsonPropertyName("date")]    public string Date    { get; set; } = "";
}

public sealed class RecordedGameInfo
{
    [JsonPropertyName("id")]          public string Id                 { get; set; } = "";
    [JsonPropertyName("isSoloGame")] public bool   IsSoloGame         { get; set; }
    [JsonPropertyName("map")]         public string Map                { get; set; } = "";
    [JsonPropertyName("alliances")]   public bool   Alliances          { get; set; }
    [JsonPropertyName("fog")]         public bool   Fog                { get; set; }
    [JsonPropertyName("blizzards")]   public bool   Blizzards          { get; set; }
    [JsonPropertyName("gameMode")]    public string GameMode           { get; set; } = "";
    [JsonPropertyName("cardType")]    public string CardType           { get; set; } = "";
    [JsonPropertyName("dice")]        public string Dice               { get; set; } = "";
    [JsonPropertyName("inactivityBehavior")] public string InactivityBehavior { get; set; } = "";
    [JsonPropertyName("portals")]      public string Portals            { get; set; } = "";
    [JsonPropertyName("gameDuration")] public long   GameDuration      { get; set; }
}

public sealed class RecordedPlayer
{
    [JsonPropertyName("lobbyIndex")]   public int    LobbyIndex   { get; set; }
    [JsonPropertyName("userId")]       public ulong  UserId       { get; set; }
    [JsonPropertyName("deviceId")]     public string DeviceId     { get; set; } = "";
    [JsonPropertyName("name")]         public string Name         { get; set; } = "";
    [JsonPropertyName("colour")]       public string Colour       { get; set; } = "";
    [JsonPropertyName("rank")]         public string Rank         { get; set; } = "";
    [JsonPropertyName("rank1v1")]      public string Rank1v1      { get; set; } = "";
    [JsonPropertyName("battlePoints")] public int    BattlePoints { get; set; }
    [JsonPropertyName("isBotted")]     public bool   IsBotted     { get; set; }
}

public sealed class RecordedRound
{
    [JsonPropertyName("mapState")]
    public Dictionary<string, TerritoryState> MapState { get; set; } = new();

    // key = player ID string
    [JsonPropertyName("players")]
    public Dictionary<string, RecordedPlayerStatus> Players { get; set; } = new();

    // Alliance state at round start: player ID → list of allied player IDs
    [JsonPropertyName("alliances")]
    public Dictionary<string, List<int>> Alliances { get; set; } = new();

    // key = player ID string
    [JsonPropertyName("playerTurns")]
    public Dictionary<string, PlayerTurnRecord> PlayerTurns { get; set; } = new();
}

public sealed class PlayerTurnRecord
{
    [JsonPropertyName("income")]         public int          Income         { get; set; }
    [JsonPropertyName("territories")]    public int          Territories    { get; set; }
    [JsonPropertyName("capitals")]       public int          Capitals       { get; set; }
    [JsonPropertyName("units")]          public int          Units          { get; set; }
    [JsonPropertyName("cardsAtTurnStart")] public List<string> CardsAtTurnStart { get; set; } = new();

    [JsonPropertyName("snapshots")]
    public List<TurnSnapshot> Snapshots { get; set; } = new();

    [JsonPropertyName("cardsAfterTurn")]
    public List<string> CardsAfterTurn { get; set; } = new();
}

// ── Turn snapshot types ───────────────────────────────────────────────────────

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(TerritoryTurnSnapshot),  "territory")]
[JsonDerivedType(typeof(AllianceTurnSnapshot),   "alliance")]
[JsonDerivedType(typeof(PlayerKilledTurnSnapshot),"player_killed")]
[JsonDerivedType(typeof(CardsTradedTurnSnapshot), "cards_traded")]
[JsonDerivedType(typeof(GameOverTurnSnapshot),    "game_over")]
public abstract class TurnSnapshot
{
    [JsonPropertyName("time")]
    public long Time { get; set; }
}

public sealed class TerritoryTurnSnapshot : TurnSnapshot
{
    [JsonPropertyName("territories")]
    public Dictionary<string, TerritoryState> Territories { get; set; } = new();
}

public sealed class AllianceTurnSnapshot : TurnSnapshot
{
    // Full picture: player ID → list of allied player IDs
    [JsonPropertyName("alliances")]
    public Dictionary<string, List<int>> Alliances { get; set; } = new();
}

public sealed class PlayerKilledTurnSnapshot : TurnSnapshot
{
    [JsonPropertyName("player")]
    public KilledPlayerInfo Player { get; set; } = new();
}

public sealed class KilledPlayerInfo
{
    [JsonPropertyName("id")]       public int          Id       { get; set; }
    [JsonPropertyName("killedBy")] public int          KilledBy { get; set; }
    [JsonPropertyName("cards")]    public List<string> Cards    { get; set; } = new();
}

public sealed class CardsTradedTurnSnapshot : TurnSnapshot
{
    [JsonPropertyName("cards")]
    public List<string> Cards { get; set; } = new();
}

public sealed class GameOverTurnSnapshot : TurnSnapshot { }

// ── Territory / player state types ───────────────────────────────────────────

public sealed class TerritoryState
{
    [JsonPropertyName("ownedBy")]        public int?   OwnedBy        { get; set; }
    [JsonPropertyName("isCapital")]      public bool   IsCapital      { get; set; }
    [JsonPropertyName("isPortal")]       public bool   IsPortal       { get; set; }
    [JsonPropertyName("isActivePortal")] public bool   IsActivePortal { get; set; }
    [JsonPropertyName("units")]          public int    Units          { get; set; }

    [JsonPropertyName("previouslyOwnedBy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? PreviouslyOwnedBy { get; set; }

    [JsonPropertyName("previousUnits")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int? PreviousUnits { get; set; }
}

public sealed class RecordedPlayerStatus
{
    [JsonPropertyName("isDead")]          public bool         IsDead          { get; set; }
    [JsonPropertyName("isTakenOverByAI")] public bool         IsTakenOverByAI { get; set; }
    [JsonPropertyName("isBotFlagged")]    public bool         IsBotFlagged    { get; set; }
    [JsonPropertyName("isQuit")]          public bool         IsQuit          { get; set; }
    [JsonPropertyName("territories")]     public int          Territories     { get; set; }
    [JsonPropertyName("capitals")]        public int          Capitals        { get; set; }
    [JsonPropertyName("units")]           public int          Units           { get; set; }
    [JsonPropertyName("cards")]           public List<string> Cards           { get; set; } = new();
}
