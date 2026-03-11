using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using RiskGameRecorder.Memory;
using RiskGameRecorder.Models;

namespace RiskGameRecorder.Recording;

public sealed class GameRecorder
{
    private enum State { Idle, Initializing, Recording }

    private State         _state          = State.Idle;
    private RecordedGame? _game;
    private GameConfig?   _config;
    private string        _activeGameId   = "";
    private int           _lastRound      = -1;
    private string        _currentTurnId  = "";

    // Maps LobbyIndex → sequential 1-based ID string (e.g. 3 → "2")
    private Dictionary<int, string> _playerIdMap = new();

    // Key of the round currently being recorded (e.g. "0", "1", ...)
    private string _currentRoundKey = "";

    // Last snapshot (sorted by name) for dedup
    private List<TerritorySnapshot>? _lastTurnSnap;
    // Full state at the last saved snapshot, for delta computation
    private Dictionary<string, TerritorySnapshot>? _prevSnapDict;

    // Last captured alliance state — for change detection
    private Dictionary<string, List<int>>? _lastAllianceSnap;

    // Previous-tick player territory counts and cards — for kill/trade detection
    private Dictionary<string, int>          _prevPlayerTerritories = new();
    private Dictionary<string, List<string>> _prevPlayerCards       = new();
    private DateTime?                        _gameOverTime;
    private bool                             _seenNoneSinceLastGame = true;

    // Elapsed time since game start — used to timestamp each snapshot
    private readonly Stopwatch _gameTimer = new();

    public void ProcessSnapshot(FullMemorySnapshot snap, GameMemoryReader reader)
    {
        switch (_state)
        {
            case State.Idle:
                if (snap.State == GameState.None) _seenNoneSinceLastGame = true;
                if (snap.State == GameState.InGame && _seenNoneSinceLastGame)
                {
                    _activeGameId = IsValidGameId(snap.GameId) ? snap.GameId : "ffffffff" + Guid.NewGuid().ToString().Substring(8);
                    _state = State.Initializing;
                }
                break;

            case State.Initializing:
                if (snap.State != GameState.InGame) { Reset(); break; }
                _config = reader.ReadGameConfig() ?? FallbackConfig(snap.GameId);
                StartRecording(snap, reader);
                _state = State.Recording;
                ProcessTick(snap, reader);
                break;

            case State.Recording:
                if (snap.State == GameState.GameOver)
                {
                    if (_gameOverTime == null) { _gameOverTime = DateTime.UtcNow; }
                    else if ((DateTime.UtcNow - _gameOverTime.Value).TotalSeconds >= 1.0) { FinalizeAndSave(); break; }
                    ProcessTick(snap, reader);
                    break;
                }
                if (snap.State != GameState.InGame)     { FinalizeAndSave(); break; }
                if (IsValidGameId(snap.GameId) && snap.GameId != _activeGameId)
                {
                    FinalizeAndSave();
                    _activeGameId = snap.GameId;
                    _state = State.Initializing;
                    break;
                }
                ProcessTick(snap, reader);
                break;
        }
    }

    public void HandleProcessDied() => FinalizeAndSave();

    public GameConfig? CurrentConfig => _config;

    public string RecorderStatus => _state switch
    {
        State.Idle         => "Not recording",
        State.Initializing => "Initializing...",
        State.Recording    => "Recording",
        _                  => ""
    };

    // ── Internals ────────────────────────────────────────────────────────────

    void StartRecording(FullMemorySnapshot snap, GameMemoryReader reader)
    {
        // Build sequential ID map: snap.Players order → 1-based sequential ID
        _playerIdMap = snap.Players
            .Select((p, i) => (p.LobbyIndex, Id: (i + 1).ToString()))
            .ToDictionary(x => x.LobbyIndex, x => x.Id);

        _game = new RecordedGame
        {
            Metadata = new RecordedMetadata
            {
                Date        = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"),
            },
            GameInfo = new RecordedGameInfo
            {
                Id                 = _activeGameId,
                IsSoloGame         = !IsValidGameId(snap.GameId),
                Map                = _config.MapName,
                Alliances          = _config.Alliances,
                Fog                = _config.FogOfWar,
                Blizzards          = _config.Blizzards,
                GameMode           = _config.GameMode,
                CardType           = _config.CardType,
                Dice               = _config.Dice,
                InactivityBehavior = _config.InactivityBehavior,
                Portals            = _config.Portals,
            },
            Players = snap.Players
                .Select((p, i) => (Id: (i + 1).ToString(), Player: new RecordedPlayer
                {
                    LobbyIndex   = p.LobbyIndex,
                    UserId       = p.UserId,
                    DeviceId     = p.DeviceId,
                    Name         = p.Name,
                    Colour       = p.ColorId,
                    Rank         = p.SkillLevel,
                    Rank1v1      = p.SkillLevel1v1,
                    BattlePoints = p.BattlePoints,
                    IsBotted     = p.IsAI,
                }))
                .ToDictionary(x => x.Id, x => x.Player),
        };

        _game.Continents = reader.ReadAllContinents();
        _game.Blizzards  = reader.ReadBlizzardTerritories();

        _gameTimer.Restart();

        _lastRound     = snap.Round;
        _currentTurnId = "";
        _lastTurnSnap  = null;
        _prevSnapDict  = snap.MapState.ToDictionary(t => t.TerritoryName);

        var alliances = BuildAllianceDict(snap);
        _lastAllianceSnap = alliances;

        _prevPlayerTerritories = snap.Players.ToDictionary(p => PlayerId(p), p => p.TerritoryCount ?? 0);
        _prevPlayerCards       = snap.Players.ToDictionary(p => PlayerId(p), p => p.TerritoryCards.Select(c => c.Type.ToString()).ToList());

        // First round
        _currentRoundKey = snap.Round.ToString();
        _game.RoundInfo[_currentRoundKey] = new RecordedRound
        {
            MapState  = ToStateList(snap.MapState),
            Players   = BuildStatuses(snap),
            Alliances = alliances,
        };
    }

    void ProcessTick(FullMemorySnapshot snap, GameMemoryReader reader)
    {
        if (_game == null) return;
        if (snap.MapState.Count == 0) return;

        var sorted        = snap.MapState.OrderBy(t => t.TerritoryName).ToList();
        bool stateChanged = _lastTurnSnap == null || !SnapshotsEqual(_lastTurnSnap, sorted);

        // Round boundary
        if (snap.Round != _lastRound && snap.Round > 0 && snap.Round > _lastRound)
        {
            // Flush any pending delta + cards to the last player before closing out the round
            if (!string.IsNullOrEmpty(_currentTurnId) && _game.RoundInfo.ContainsKey(_currentRoundKey))
            {
                var prevRound = _game.RoundInfo[_currentRoundKey];
                if (prevRound.PlayerTurns.ContainsKey(_currentTurnId))
                {
                    if (stateChanged)
                    {
                        var delta = ComputeDelta(sorted, _prevSnapDict);
                        if (delta.Count > 0)
                            prevRound.PlayerTurns[_currentTurnId].Snapshots.Add(new TerritoryTurnSnapshot { Time = SnapTime(), Territories = ToDeltaStateList(delta, _prevSnapDict) });
                    }
                    prevRound.PlayerTurns[_currentTurnId].CardsAfterTurn = PlayerCards(snap, _currentTurnId);
                }
            }

            _lastRound     = snap.Round;
            _currentTurnId = "";
            _lastTurnSnap  = null;
            _prevSnapDict  = sorted.ToDictionary(t => t.TerritoryName);

            var alliances = BuildAllianceDict(snap);
            _lastAllianceSnap = alliances;

            _currentRoundKey = snap.Round.ToString();
            _game.RoundInfo[_currentRoundKey] = new RecordedRound
            {
                MapState  = ToStateList(snap.MapState),
                Players   = BuildStatuses(snap),
                Alliances = alliances,
            };
            return;
        }

        if (!_game.RoundInfo.ContainsKey(_currentRoundKey)) return;
        var round = _game.RoundInfo[_currentRoundKey];

        // Player turn tracking — CurrentPlayerId from snapshot is a lobbyIndex string; translate it
        var turningId = TranslateId(snap.CurrentPlayerId);
        if (string.IsNullOrEmpty(turningId) && !string.IsNullOrEmpty(snap.CurrentPlayerName))
            turningId = snap.Players
                .Where(p => p.Name == snap.CurrentPlayerName)
                .Select(PlayerId)
                .FirstOrDefault() ?? "";

        if (!string.IsNullOrEmpty(turningId) && turningId != _currentTurnId)
        {
            // Flush any state change + cards to the old player before switching turns
            if (!string.IsNullOrEmpty(_currentTurnId) && round.PlayerTurns.ContainsKey(_currentTurnId))
            {
                if (stateChanged)
                {
                    var delta = ComputeDelta(sorted, _prevSnapDict);
                    if (delta.Count > 0)
                        round.PlayerTurns[_currentTurnId].Snapshots.Add(new TerritoryTurnSnapshot { Time = SnapTime(), Territories = ToDeltaStateList(delta, _prevSnapDict) });
                    _lastTurnSnap = sorted;
                    _prevSnapDict = sorted.ToDictionary(t => t.TerritoryName);
                    stateChanged  = false;
                }
                round.PlayerTurns[_currentTurnId].CardsAfterTurn = PlayerCards(snap, _currentTurnId);
            }

            _currentTurnId = turningId;

            if (!round.PlayerTurns.ContainsKey(turningId))
            {
                var player       = snap.Players.FirstOrDefault(p => PlayerId(p) == turningId);
                var turnStartCards = player?.TerritoryCards.Select(c => c.Type.ToString()).ToList() ?? new();

                // Reseed _prevPlayerCards for this player so cards_traded detection
                // is based on the actual state at turn start, not the last-polled state.
                _prevPlayerCards[turningId] = turnStartCards;

                round.PlayerTurns[turningId] = new PlayerTurnRecord
                {
                    Income           = player != null ? ComputeIncome(player) : 0,
                    Territories      = player?.TerritoryCount ?? 0,
                    Capitals         = player?.CapitalCount   ?? 0,
                    Units            = player?.Units          ?? 0,
                    CardsAtTurnStart = turnStartCards,
                };
            }
        }

        // Save delta snapshot for current player
        if (!string.IsNullOrEmpty(_currentTurnId) && stateChanged)
        {
            var delta = ComputeDelta(sorted, _prevSnapDict);
            var prevForDelta = _prevSnapDict;
            _lastTurnSnap = sorted;
            _prevSnapDict = sorted.ToDictionary(t => t.TerritoryName);
            if (delta.Count > 0)
                round.PlayerTurns[_currentTurnId].Snapshots.Add(new TerritoryTurnSnapshot { Time = SnapTime(), Territories = ToDeltaStateList(delta, prevForDelta) });
        }

        if (snap.State == GameState.InGame && !string.IsNullOrEmpty(_currentTurnId) && round.PlayerTurns.ContainsKey(_currentTurnId))
        {
            var currentAlliances = BuildAllianceDict(snap);
            if (_lastAllianceSnap == null || !AlliancesEqual(_lastAllianceSnap, currentAlliances))
            {
                round.PlayerTurns[_currentTurnId].Snapshots.Add(new AllianceTurnSnapshot { Time = SnapTime(), Alliances = currentAlliances });
                _lastAllianceSnap = currentAlliances;
            }
        }

        // Check for player kills — any player whose territory count just hit 0
        if (!string.IsNullOrEmpty(_currentTurnId) && round.PlayerTurns.ContainsKey(_currentTurnId))
        {
            int killedByInt = int.TryParse(_currentTurnId, out var kbi) ? kbi : 0;
            foreach (var player in snap.Players)
            {
                var pid         = PlayerId(player);
                int currentTerr = player.TerritoryCount ?? 0;
                if (_prevPlayerTerritories.TryGetValue(pid, out var prevTerr) && prevTerr > 0 && currentTerr == 0)
                {
                    var prevCards   = _prevPlayerCards.TryGetValue(pid, out var pc) ? pc : new List<string>();
                    int killedIdInt = int.TryParse(pid, out var ki) ? ki : 0;
                    round.PlayerTurns[_currentTurnId].Snapshots.Add(new PlayerKilledTurnSnapshot
                    {
                        Time   = SnapTime(),
                        Player = new KilledPlayerInfo { Id = killedIdInt, KilledBy = killedByInt, Cards = prevCards },
                    });
                }
            }
        }

        // Check for cards traded — current player loses 3+ cards in one tick
        if (!string.IsNullOrEmpty(_currentTurnId) && round.PlayerTurns.ContainsKey(_currentTurnId))
        {
            var currentPlayer = snap.Players.FirstOrDefault(p => PlayerId(p) == _currentTurnId);
            if (currentPlayer != null && _prevPlayerCards.TryGetValue(_currentTurnId, out var prevCards))
            {
                var currentCards = currentPlayer.TerritoryCards.Select(c => c.Type.ToString()).ToList();
                int lost         = prevCards.Count - currentCards.Count;
                if (lost >= 3)
                {
                    // Determine which cards were removed by set-difference against previous
                    var remaining = new List<string>(prevCards);
                    foreach (var card in currentCards) remaining.Remove(card);
                    round.PlayerTurns[_currentTurnId].Snapshots.Add(new CardsTradedTurnSnapshot
                    {
                        Time  = SnapTime(),
                        Cards = remaining,
                    });
                }
            }
        }

        // Update per-player tracking for next tick
        foreach (var player in snap.Players)
        {
            var pid = PlayerId(player);
            _prevPlayerTerritories[pid] = player.TerritoryCount ?? 0;
            _prevPlayerCards[pid]       = player.TerritoryCards.Select(c => c.Type.ToString()).ToList();
        }
    }

    void FinalizeAndSave()
    {
        var game = _game;
        _game = null;

        if (game == null || game.RoundInfo.Count == 0) { Reset(); return; }

        game.GameInfo.GameDuration = SnapTime();

        if (!string.IsNullOrEmpty(_currentTurnId) && game.RoundInfo.ContainsKey(_currentRoundKey))
        {
            var lastRound = game.RoundInfo[_currentRoundKey];
            if (lastRound.PlayerTurns.TryGetValue(_currentTurnId, out var turn))
                turn.Snapshots.Add(new GameOverTurnSnapshot { Time = SnapTime() });
        }

        var cfg = _config!;
        Reset();

        try { SaveToDisk(game, cfg); }
        catch (Exception ex) { Debug.WriteLine($"[GameRecorder] Save failed: {ex.Message}"); }
    }

    void SaveToDisk(RecordedGame game, GameConfig cfg)
    {
        var rawMap    = cfg.MapName.Contains('/') ? cfg.MapName.Split('/')[^1] : cfg.MapName;
        var mapName   = IsValidString(rawMap) ? ToPascalCase(rawMap) : "unknown";
        int version   = game.Metadata.Version;
        var filename  = $"{DateTime.Now:yyyy-MM-dd--HH-mm}-{mapName}-{cfg.GameMode}-{cfg.CardType}-v{version}.json";

        var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GameReplays");
        Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(game, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dir, filename), json);
        Debug.WriteLine($"[GameRecorder] Saved to {filename}");
    }

    void Reset()
    {
        _game             = null;
        _config           = null;
        _activeGameId     = "";
        _lastRound        = -1;
        _currentRoundKey  = "";
        _currentTurnId    = "";
        _lastTurnSnap     = null;
        _prevSnapDict     = null;
        _playerIdMap           = new();
        _lastAllianceSnap      = null;
        _prevPlayerTerritories   = new();
        _prevPlayerCards         = new();
        _gameOverTime            = null;
        _seenNoneSinceLastGame   = false;
        _gameTimer.Reset();
        _state            = State.Idle;
    }

    // ── ID helpers ───────────────────────────────────────────────────────────

    // LobbyIndex → sequential string ID (e.g. "2")
    string PlayerId(PlayerModel m) =>
        _playerIdMap.TryGetValue(m.LobbyIndex, out var id) ? id : m.LobbyIndex.ToString();

    // lobbyIndex string (from GameMemoryReader) → sequential string ID
    string TranslateId(string lobbyIdxStr) =>
        int.TryParse(lobbyIdxStr, out var li) && _playerIdMap.TryGetValue(li, out var id) ? id : lobbyIdxStr;

    // lobbyIndex string → sequential int? (for ownedBy fields in JSON)
    int? ToSeqInt(string lobbyIdxStr) =>
        int.TryParse(lobbyIdxStr, out var li) && _playerIdMap.TryGetValue(li, out var id) && int.TryParse(id, out var seqInt)
            ? seqInt : (int?)null;

    // ── Helpers ──────────────────────────────────────────────────────────────

    static bool IsValidGameId(string id) =>
        !string.IsNullOrEmpty(id) && id != "(empty)" && id != "(invalid)";

    static bool IsValidString(string s) =>
        !string.IsNullOrEmpty(s) && s != "(empty)" && s != "(invalid)";

    static GameConfig FallbackConfig(string gameId) =>
        new(gameId, "unknown", "unknown", "unknown", "unknown", "unknown", "unknown", false, false, false);

    Dictionary<string, TerritoryState> ToStateList(List<TerritorySnapshot> snaps) =>
        snaps.ToDictionary(
            t => t.TerritoryName,
            t => new TerritoryState
            {
                OwnedBy        = ToSeqInt(t.OwnedBy),
                IsCapital      = t.IsCapital,
                IsPortal       = t.IsPortal,
                IsActivePortal = t.IsActivePortal,
                Units          = t.Units,
            });

    Dictionary<string, TerritoryState> ToDeltaStateList(List<TerritorySnapshot> delta, Dictionary<string, TerritorySnapshot>? prev) =>
        delta.ToDictionary(
            t => t.TerritoryName,
            t =>
            {
                TerritorySnapshot old = null;
                prev?.TryGetValue(t.TerritoryName, out old);
                return new TerritoryState
                {
                    OwnedBy           = ToSeqInt(t.OwnedBy),
                    // If the territory just changed hands and was previously a capital, preserve the flag —
                    // the game clears IsCapital in memory as soon as it's captured.
                    IsCapital         = t.IsCapital || (old != null && old.OwnedBy != t.OwnedBy && old.IsCapital),
                    IsPortal          = t.IsPortal,
                    IsActivePortal    = t.IsActivePortal,
                    Units             = t.Units,
                    PreviouslyOwnedBy = old != null && old.OwnedBy != t.OwnedBy ? ToSeqInt(old.OwnedBy) : null,
                    PreviousUnits     = old != null && old.Units != t.Units ? old.Units : (int?)null,
                };
            });

    Dictionary<string, RecordedPlayerStatus> BuildStatuses(FullMemorySnapshot snap) =>
        snap.Players.ToDictionary(
            PlayerId,
            p => new RecordedPlayerStatus
            {
                IsDead          = p.TerritoryCount.HasValue && p.TerritoryCount == 0,
                IsTakenOverByAI = p.IsCurrentlyAutomated && !p.IsAI,
                IsBotFlagged    = p.IsBottedOut,
                IsQuit          = p.IsQuit,
                Territories     = p.TerritoryCount ?? 0,
                Capitals        = p.CapitalCount   ?? 0,
                Units           = p.Units          ?? 0,
                Cards           = p.TerritoryCards.Select(c => c.Type.ToString()).ToList(),
            });

    Dictionary<string, List<int>> BuildAllianceDict(FullMemorySnapshot snap)
    {
        var nameToSeqId = snap.Players
            .Where(p => _playerIdMap.ContainsKey(p.LobbyIndex))
            .ToDictionary(p => p.Name, p => int.Parse(PlayerId(p)));
        return snap.Players
            .Where(p => _playerIdMap.ContainsKey(p.LobbyIndex))
            .ToDictionary(
                PlayerId,
                p => p.AllyNames
                    .Where(name => nameToSeqId.ContainsKey(name))
                    .Select(name => nameToSeqId[name])
                    .ToList());
    }

    static bool AlliancesEqual(Dictionary<string, List<int>> a, Dictionary<string, List<int>> b)
    {
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv)) return false;
            if (!v.SequenceEqual(bv)) return false;
        }
        return true;
    }

    List<string> PlayerCards(FullMemorySnapshot snap, string playerId) =>
        snap.Players.FirstOrDefault(p => PlayerId(p) == playerId)
            ?.TerritoryCards.Select(c => c.Type.ToString()).ToList() ?? new();

    static int ComputeIncome(PlayerModel p)
    {
        if (p.TerritoryCount.HasValue && p.TerritoryCount.Value == 0) return 0;
        int territoryBonus = Math.Max(0, ((p.TerritoryCount ?? 0) - 9) / 3);
        int continentBonus = p.Continents?.Sum(c => c.Bonus) ?? 0;
        return 3 + (p.CapitalCount ?? 0) * 2 + continentBonus + territoryBonus;
    }

    static List<TerritorySnapshot> ComputeDelta(List<TerritorySnapshot> current, Dictionary<string, TerritorySnapshot>? prev)
    {
        if (prev == null) return current;
        return current.Where(t => !prev.TryGetValue(t.TerritoryName, out var old) || old != t).ToList();
    }

    static bool SnapshotsEqual(List<TerritorySnapshot> a, List<TerritorySnapshot> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return false;
        return true;
    }



    long SnapTime() => (_gameTimer.ElapsedMilliseconds / 50) * 50;

    static string ToPascalCase(string s)
    {
        var sb = new System.Text.StringBuilder();
        bool nextUpper = true;
        foreach (char c in s)
        {
            if (c == ' ')              { nextUpper = true; continue; }
            if (!char.IsLetterOrDigit(c)) continue;
            sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
            nextUpper = false;
        }
        return sb.ToString();
    }
}
