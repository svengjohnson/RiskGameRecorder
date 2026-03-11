
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using RiskGameRecorder.Models;
using RiskGameRecorder.Recording;

namespace RiskGameRecorder.Memory;

public sealed class GameMemoryReader
{
    private readonly Process _proc;
    private readonly IntPtr _process;
    private readonly IntPtr _gameAssembly;

    public bool IsAlive => !_proc.HasExited;

    public GameMemoryReader()
    {
        _proc = Process.GetProcessesByName("RISK").First();
        _process = Win32.OpenProcess(Win32.ACCESS_READ, false, _proc.Id);
        _gameAssembly = _proc.Modules.Cast<ProcessModule>()
            .First(m => m.ModuleName == "GameAssembly.dll").BaseAddress;
    }

    IntPtr ReadPtr(IntPtr addr)
    {
        if (addr == IntPtr.Zero) return IntPtr.Zero;
        var buf = new byte[8];
        Win32.ReadProcessMemory(_process, addr, buf, 8, out _);
        return (IntPtr)BitConverter.ToInt64(buf, 0);
    }

    int ReadInt32(IntPtr addr)
    {
        var buf = new byte[4];
        Win32.ReadProcessMemory(_process, addr, buf, 4, out _);
        return BitConverter.ToInt32(buf, 0);
    }

    ulong ReadUInt64(IntPtr addr)
    {
        var buf = new byte[8];
        Win32.ReadProcessMemory(_process, addr, buf, 8, out _);
        return BitConverter.ToUInt64(buf, 0);
    }

    int ReadObscuredInt(IntPtr addr)
    {
        return ReadInt32(addr + Offsets.OBSCURED_INT_FAKE);
    }

    string ReadIl2CppString(IntPtr ptr)
    {
        if (ptr == IntPtr.Zero) return "(empty)";
        int len = ReadInt32(ptr + 0x10);
        if (len <= 0 || len > 256) return "(invalid)";
        var buf = new byte[len * 2];
        Win32.ReadProcessMemory(_process, ptr + 0x14, buf, buf.Length, out _);
        return Encoding.Unicode.GetString(buf);
    }

    string ReadGameId()
    {
        var pnStatic = ReadPtr(ReadPtr(_gameAssembly + (int)Offsets.PHOTON_NETWORK_STATIC) + Offsets.OFFSET_SINGLETON);
        if (pnStatic == IntPtr.Zero) return "";
        var peer = ReadPtr(pnStatic + Offsets.PHOTON_NETWORKING_PEER);
        if (peer == IntPtr.Zero) return "";
        var room = ReadPtr(peer + Offsets.NETWORKING_PEER_ROOM);
        if (room == IntPtr.Zero) return "";
        return ReadIl2CppString(ReadPtr(room + Offsets.ROOM_NAME));
    }

    public (List<PlayerModel> Players, GameState State, int Round, string CurrentPlayerName, string GameId) ReadPlayers()
    {
        var result = new List<PlayerModel>();

        var ptrs = ReadPlayerPointersFromGameManager();
        if (ptrs.Count > 0)
        {
            var gmStatic = ReadPtr(ReadPtr(_gameAssembly + (int)Offsets.GAME_MANAGER_STATIC) + Offsets.OFFSET_SINGLETON);

            var hasEndedBuf = new byte[1];
            Win32.ReadProcessMemory(_process, gmStatic + Offsets.GAME_MANAGER_HAS_ENDED, hasEndedBuf, 1, out _);
            bool hasEnded = hasEndedBuf[0] != 0;

            var pairs = new List<(IntPtr ptr, PlayerModel model)>();
            foreach (var p in ptrs)
            {
                var model = ReadPlayerModel(p, true);
                if (model != null)
                {
                    result.Add(model);
                    pairs.Add((p, model));
                }
            }

            if (hasEnded)
                return (result, GameState.GameOver, 0, "", "");

            var ptrToModel = pairs.ToDictionary(x => x.ptr, x => x.model);
            foreach (var (ptr, model) in pairs)
                model.AllyNames = ReadAllyNames(ptr, ptrToModel);

            int round = ReadInt32(gmStatic + Offsets.GAME_MANAGER_CURRENT_ROUND) + 1;

            string currentPlayerName = "";
            var gameControl = ReadPtr(gmStatic + Offsets.GAME_MANAGER_GAME_CONTROL);
            if (gameControl != IntPtr.Zero)
            {
                var currentPlayerPtr = ReadPtr(gameControl + Offsets.GAMECONTROL_PLAYER);
                currentPlayerName = pairs.FirstOrDefault(p => p.ptr == currentPlayerPtr).model?.Name ?? "";
            }

            string gameId = ReadGameId();
            return (result, GameState.InGame, round, currentPlayerName, gameId);
        }

        var lobbyPtrs = ReadPlayerPointersFromPlayerSelectScreen();
        if (lobbyPtrs.Count > 0)
        {
            foreach (var p in lobbyPtrs)
            {
                var model = ReadPlayerModel(p, false);
                if (model != null)
                    result.Add(model);
            }
            return (result, GameState.InLobby, 0, "", "");
        }

        return (result, GameState.None, 0, "", "");
    }

    public List<IntPtr> ReadPlayerPointersFromPlayerSelectScreen()
    {
        var result = new List<IntPtr>();

        var uiTypeInfo     = ReadPtr(_gameAssembly + (int)Offsets.UI_MANAGER_STATIC);
        if (uiTypeInfo == IntPtr.Zero) return result;
        var uiStaticFields = ReadPtr(uiTypeInfo + Offsets.OFFSET_SINGLETON);
        if (uiStaticFields == IntPtr.Zero) return result;

        var statesDict = ReadPtr(uiStaticFields + Offsets.UIMANAGER_STATIC_STATES);
        if (statesDict == IntPtr.Zero) return result;

        var entries = ReadPtr(statesDict + Offsets.DICT_ENTRIES);
        if (entries == IntPtr.Zero) return result;
        int count = ReadInt32(statesDict + Offsets.DICT_COUNT);
        if (count <= 0 || count > 64) return result;

        var playerSelectScreen = IntPtr.Zero;
        for (int i = 0; i < count; i++)
        {
            var entry    = entries + Offsets.ARRAY_DATA + i * Offsets.DICT_ENTRY_SIZE;
            int hashCode = ReadInt32(entry + Offsets.DICT_ENTRY_HASHCODE);
            if (hashCode < 0) continue;
            int key = ReadInt32(entry + Offsets.DICT_ENTRY_KEY);
            if (key == Offsets.ESTATE_SETTINGS_PLAYERS)
            {
                playerSelectScreen = ReadPtr(entry + Offsets.DICT_ENTRY_VALUE);
                break;
            }
        }

        if (playerSelectScreen == IntPtr.Zero) return result;

        var playerInfoList = ReadPtr(playerSelectScreen + Offsets.PLAYERSELECT_PLAYER_INFO_LIST);
        if (playerInfoList == IntPtr.Zero) return result;

        var items = ReadPtr(playerInfoList + Offsets.LIST_ITEMS);
        int size  = ReadInt32(playerInfoList + Offsets.LIST_SIZE);
        if (items == IntPtr.Zero || size <= 0 || size > 8) return result;

        for (int i = 0; i < size; i++)
        {
            var info = ReadPtr(items + Offsets.ARRAY_DATA + i * 8);
            if (info != IntPtr.Zero)
                result.Add(info + Offsets.OFFSET_PLAYERS_PHOTON);
        }

        return result;
    }
    
    List<IntPtr> ReadPlayerPointersFromGameManager()
    {
        var result = new List<IntPtr>();

        var gmStatic = ReadPtr(_gameAssembly + (int)Offsets.GAME_MANAGER_STATIC);
        if (gmStatic == IntPtr.Zero) return result;

        var gm = ReadPtr(gmStatic + Offsets.OFFSET_SINGLETON);
        if (gm == IntPtr.Zero) return result;

        var players = ReadPtr(gm + Offsets.OFFSET_PLAYERS);
        if (players == IntPtr.Zero) return result;

        int count = ReadInt32(players + Offsets.ARRAY_LENGTH);
        if (count <= 0 || count > 32) return result;

        for (int i = 0; i < count; i++)
        {
            var p = ReadPtr(players + Offsets.ARRAY_DATA + i * 8);
            if (p != IntPtr.Zero)
                result.Add(p);
        }

        return result;
    }
    
    PlayerModel ReadPlayerModel(IntPtr p, bool withIngameData)
    {
        int offsetAdjustment = withIngameData ? 0 : Offsets.OFFSET_PLAYERS_PHOTON;
        IntPtr nameOffset = 0x18;
        
        var model = new PlayerModel
        {
            Name = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_NAME - offsetAdjustment)),
            UserId = ReadUInt64(p + Offsets.OFFSET_USERID - offsetAdjustment),
            DeviceId = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_DEVICEID - offsetAdjustment)),
            ColorId = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_COLOR_ID - offsetAdjustment)),
            SkillLevel = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_SKILL_LEVEL - offsetAdjustment)),
            SkillLevel1v1 = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_SKILL_LEVEL_1V1 - offsetAdjustment)),
            BattlePoints = ReadInt32(p + Offsets.OFFSET_BATTLE_POINTS - offsetAdjustment)
        };

        if (withIngameData)
        {
            model.LobbyIndex = ReadInt32(p + Offsets.OFFSET_LOBBY_INDEX);
            var flagBuf = new byte[2];
            Win32.ReadProcessMemory(_process, p + Offsets.OFFSET_IS_QUIT, flagBuf, 2, out _);
            model.IsQuit               = flagBuf[0] != 0;
            model.IsBottedOut          = flagBuf[1] != 0;
            model.IsAI                 = ReadPtr(p + Offsets.OFFSET_AI) != IntPtr.Zero;
            var automatedBuf = new byte[1];
            Win32.ReadProcessMemory(_process, p + Offsets.OFFSET_IS_CURRENTLY_AUTOMATED, automatedBuf, 1, out _);
            model.IsCurrentlyAutomated = automatedBuf[0] != 0;
            var (terrs, conts) = ReadPlayerMapData(p);
            model.Territories    = terrs;
            model.Continents     = conts;
            model.Units          = terrs.Sum(t => t.Units);
            model.CapitalUnits   = terrs.Where(t => t.IsCapital).Sum(t => t.Units);
            model.TerritoryCount = terrs.Count;
            model.CapitalCount   = terrs.Count(t => t.IsCapital);
            ReadTerritoryCards(p, model);
        }

        return model;
    }
    

    (List<Models.TerritoryInfo> territories, List<Models.ContinentInfo> continents) ReadPlayerMapData(IntPtr playerPtr)
    {
        var territories = new List<Models.TerritoryInfo>();
        var continents  = new List<Models.ContinentInfo>();

        var mmTypeInfo = ReadPtr(_gameAssembly + (int)Offsets.MAP_MANAGER_STATIC);
        if (mmTypeInfo == IntPtr.Zero) return (territories, continents);
        var mmStatic   = ReadPtr(mmTypeInfo + Offsets.OFFSET_SINGLETON);
        if (mmStatic == IntPtr.Zero) return (territories, continents);
        var map        = ReadPtr(mmStatic + Offsets.MAP_MANAGER_MAP);
        if (map == IntPtr.Zero) return (territories, continents);

        var terrArray = ReadPtr(map + Offsets.MAP_TERRITORIES);
        if (terrArray != IntPtr.Zero)
        {
            int count = ReadInt32(terrArray + Offsets.ARRAY_LENGTH);
            var capBuf = new byte[1];
            for (int i = 0; i < count && count <= 512; i++)
            {
                var territory = ReadPtr(terrArray + Offsets.ARRAY_DATA + i * 8);
                if (territory == IntPtr.Zero) continue;
                if (ReadPtr(territory + Offsets.TERRITORY_PLAYER) != playerPtr) continue;

                int units = ReadObscuredInt(territory + Offsets.TERRITORY_UNITS);
                Win32.ReadProcessMemory(_process, territory + Offsets.TERRITORY_IS_CAPITAL, capBuf, 1, out _);
                territories.Add(new Models.TerritoryInfo
                {
                    Name = ReadIl2CppString(ReadPtr(territory + Offsets.TERRITORY_NAME)).Trim(),
                    Units = units,
                    IsCapital = capBuf[0] != 0
                });
            }
        }

        var regionArray = ReadPtr(map + Offsets.MAP_REGIONS);
        if (regionArray != IntPtr.Zero)
        {
            int regionCount = ReadInt32(regionArray + Offsets.ARRAY_LENGTH);
            for (int i = 0; i < regionCount && regionCount <= 64; i++)
            {
                var region = ReadPtr(regionArray + Offsets.ARRAY_DATA + i * 8);
                if (region == IntPtr.Zero) continue;

                var regionTerrs = ReadPtr(region + Offsets.REGION_TERRITORIES);
                if (regionTerrs == IntPtr.Zero) continue;
                int terrCount = ReadInt32(regionTerrs + Offsets.ARRAY_LENGTH);
                if (terrCount <= 0 || terrCount > 128) continue;

                bool ownsAll = true;
                var blockerBuf = new byte[1];
                for (int j = 0; j < terrCount; j++)
                {
                    var t = ReadPtr(regionTerrs + Offsets.ARRAY_DATA + j * 8);
                    if (t == IntPtr.Zero) { ownsAll = false; break; }
                    Win32.ReadProcessMemory(_process, t + Offsets.TERRITORY_IS_BLOCKER, blockerBuf, 1, out _);
                    if (blockerBuf[0] != 0) continue;
                    if (ReadPtr(t + Offsets.TERRITORY_PLAYER) != playerPtr) { ownsAll = false; break; }
                }
                if (!ownsAll) continue;

                continents.Add(new Models.ContinentInfo
                {
                    Name  = ReadIl2CppString(ReadPtr(region + Offsets.REGION_NAME)),
                    Bonus = ReadInt32(region + Offsets.REGION_CAPTURE_BONUS)
                });
            }
        }

        return (territories, continents);
    }

    List<string> ReadAllyNames(IntPtr playerPtr, Dictionary<IntPtr, PlayerModel> ptrToModel)
    {
        var result = new List<string>();
        var dict = ReadPtr(playerPtr + Offsets.PLAYER_SOCIAL_INFO);
        if (dict == IntPtr.Zero) return result;

        var entries = ReadPtr(dict + Offsets.DICT_ENTRIES);
        if (entries == IntPtr.Zero) return result;
        int count = ReadInt32(dict + Offsets.DICT_COUNT);
        if (count <= 0 || count > 32) return result;

        for (int i = 0; i < count; i++)
        {
            var entry    = entries + Offsets.ARRAY_DATA + i * Offsets.DICT_ENTRY_SIZE;
            int hashCode = ReadInt32(entry + Offsets.DICT_ENTRY_HASHCODE);
            if (hashCode < 0) continue;

            var keyPtr   = ReadPtr(entry + Offsets.DICT_ENTRY_KEY);
            var valuePtr = ReadPtr(entry + Offsets.DICT_ENTRY_VALUE);
            if (valuePtr == IntPtr.Zero) continue;

            int allianceState = ReadInt32(valuePtr + Offsets.SOCIAL_INFO_ALLIANCE_STATE);
            if (allianceState == 3 && ptrToModel.TryGetValue(keyPtr, out var ally))
                result.Add(ally.Name);
        }
        return result;
    }

    void ReadTerritoryCards(IntPtr playerPtr, PlayerModel model)
    {
        var list = ReadPtr(playerPtr + Offsets.OFFSET_PLAYER_CARDS);
        if (list == IntPtr.Zero) return;

        int size = ReadInt32(list + Offsets.LIST_SIZE);
        var items = ReadPtr(list + Offsets.LIST_ITEMS);

        for (int i = 0; i < size; i++)
        {
            var card = ReadPtr(items + Offsets.ARRAY_DATA + i * 8);
            if (card == IntPtr.Zero) continue;

            model.TerritoryCards.Add(new TerritoryCard
            {
                Type = (TerritoryCardType)ReadInt32(card + Offsets.TERRITORYCARD_TYPE)
            });
        }
    }

    static string PlayerId(PlayerModel m) =>
        m.UserId != 0 ? m.UserId.ToString() : $"bot_{m.LobbyIndex}";

    IntPtr GetMap()
    {
        var mmStatic = ReadPtr(ReadPtr(_gameAssembly + (int)Offsets.MAP_MANAGER_STATIC) + Offsets.OFFSET_SINGLETON);
        return mmStatic == IntPtr.Zero ? IntPtr.Zero : ReadPtr(mmStatic + Offsets.MAP_MANAGER_MAP);
    }

    public GameConfig? ReadGameConfig()
    {
        var gmStatic = ReadPtr(ReadPtr(_gameAssembly + (int)Offsets.GAME_MANAGER_STATIC) + Offsets.OFFSET_SINGLETON);
        if (gmStatic == IntPtr.Zero) return null;

        int gameMode  = ReadInt32(gmStatic + Offsets.GM_GAME_MODE);
        int diceRolls = ReadInt32(gmStatic + Offsets.GM_DICE_ROLLS);
        int portals   = ReadInt32(gmStatic + Offsets.GM_PORTALS);
        int inactivity = ReadInt32(gmStatic + Offsets.GM_INACTIVITY_BEHAVIOR);

        var flagsBuf = new byte[3];
        Win32.ReadProcessMemory(_process, gmStatic + Offsets.GM_IS_FOG_OF_WAR, flagsBuf, 3, out _);
        bool isFog      = flagsBuf[0] != 0;
        bool isBlizz    = flagsBuf[1] != 0;
        bool isAlliance = flagsBuf[2] != 0;

        int cardMode = ReadInt32(gmStatic + Offsets.GM_TERRITORY_CARD_CONFIG);

        string mapName = "";
        var map = GetMap();
        if (map != IntPtr.Zero)
        {
            var mapInfo = ReadPtr(map + Offsets.MAP_MAP_INFO);
            if (mapInfo != IntPtr.Zero)
            {
                var trans = ReadPtr(mapInfo + Offsets.MAP_INFO_NAME_TRANS);
                if (trans != IntPtr.Zero)
                    mapName = ReadIl2CppString(ReadPtr(trans + Offsets.DTRANSLATION_EN));
                if (string.IsNullOrEmpty(mapName) || mapName == "(empty)" || mapName == "(invalid)")
                    mapName = ReadIl2CppString(ReadPtr(mapInfo + Offsets.MAP_INFO_NAME_ID));
            }
        }

        return new GameConfig(
            GameId:             ReadGameId(),
            MapName:            mapName,
            GameMode:           GameModeString(gameMode),
            CardType:           CardModeString(cardMode),
            Dice:               diceRolls == 1 ? "BalancedBlitz" : "TrueRandom",
            InactivityBehavior: inactivity == 0 ? "Automated" : "Neutral",
            Portals:            portals switch { 1 => "Static", 2 => "Dynamic", _ => "Off" },
            FogOfWar:           isFog,
            Blizzards:          isBlizz,
            Alliances:          isAlliance
        );
    }


    public List<string> ReadBlizzardTerritories()
    {
        var result = new List<string>();
        var map = GetMap();
        if (map == IntPtr.Zero) return result;

        var list  = ReadPtr(map + Offsets.MAP_BLOCKED_TERRITORIES);
        if (list == IntPtr.Zero) return result;
        int size  = ReadInt32(list + Offsets.LIST_SIZE);
        var items = ReadPtr(list + Offsets.LIST_ITEMS);
        if (items == IntPtr.Zero || size <= 0 || size > 512) return result;

        for (int i = 0; i < size; i++)
        {
            var t = ReadPtr(items + Offsets.ARRAY_DATA + i * 8);
            if (t != IntPtr.Zero)
                result.Add(ReadIl2CppString(ReadPtr(t + Offsets.TERRITORY_NAME)).Trim());
        }
        return result;
    }


    public Dictionary<string, int> ReadAllContinents()
    {
        var result = new Dictionary<string, int>();
        var map = GetMap();
        if (map == IntPtr.Zero) return result;

        var regionArray = ReadPtr(map + Offsets.MAP_REGIONS);
        if (regionArray == IntPtr.Zero) return result;
        int regionCount = ReadInt32(regionArray + Offsets.ARRAY_LENGTH);
        if (regionCount <= 0 || regionCount > 64) return result;

        for (int i = 0; i < regionCount; i++)
        {
            var region = ReadPtr(regionArray + Offsets.ARRAY_DATA + i * 8);
            if (region == IntPtr.Zero) continue;
            var name = ReadIl2CppString(ReadPtr(region + Offsets.REGION_NAME));
            if (string.IsNullOrEmpty(name) || name == "(empty)" || name == "(invalid)") continue;
            int bonus = ReadInt32(region + Offsets.REGION_CAPTURE_BONUS);
            result[name] = bonus;
        }
        return result;
    }

    public FullMemorySnapshot ReadFullSnapshot()
    {
        var (players, state, round, currentPlayerName, gameId) = ReadPlayers();

        if (state != GameState.InGame && state != GameState.GameOver)
            return new FullMemorySnapshot { Players = players, State = state, Round = round,
                CurrentPlayerName = currentPlayerName, GameId = gameId };

        var ptrs = ReadPlayerPointersFromGameManager();
        var ptrToId = new Dictionary<IntPtr, string>(ptrs.Count);
        string currentPlayerId = "";
        foreach (var p in ptrs)
        {
            var lobbyIdx = ReadInt32(p + Offsets.OFFSET_LOBBY_INDEX);
            var id       = lobbyIdx.ToString();
            ptrToId.TryAdd(p, id);

            if (string.IsNullOrEmpty(currentPlayerId))
            {
                var name = ReadIl2CppString(ReadPtr(p + Offsets.OFFSET_NAME));
                if (name == currentPlayerName)
                    currentPlayerId = id;
            }
        }

        var map = GetMap();
        var mapState = new List<TerritorySnapshot>();
        if (map != IntPtr.Zero)
        {
            var terrArray = ReadPtr(map + Offsets.MAP_TERRITORIES);
            if (terrArray != IntPtr.Zero)
            {
                int count = ReadInt32(terrArray + Offsets.ARRAY_LENGTH);
                var buf4  = new byte[4];
                var buf1  = new byte[1];
                for (int i = 0; i < count && count <= 512; i++)
                {
                    var t = ReadPtr(terrArray + Offsets.ARRAY_DATA + i * 8);
                    if (t == IntPtr.Zero) continue;
                    Win32.ReadProcessMemory(_process, t + Offsets.TERRITORY_IS_BLOCKER, buf1, 1, out _);
                    if (buf1[0] != 0) continue;

                    var ownerPtr = ReadPtr(t + Offsets.TERRITORY_PLAYER);
                    ptrToId.TryGetValue(ownerPtr, out var ownedBy);

                    int units = ReadObscuredInt(t + Offsets.TERRITORY_UNITS);
                    Win32.ReadProcessMemory(_process, t + Offsets.TERRITORY_IS_CAPITAL, buf1, 1, out _);
                    bool isCapital = buf1[0] != 0;
                    Win32.ReadProcessMemory(_process, t + Offsets.TERRITORY_PORTAL_STATE, buf4, 4, out _);
                    int portalState = BitConverter.ToInt32(buf4, 0);

                    mapState.Add(new TerritorySnapshot(
                        TerritoryName: ReadIl2CppString(ReadPtr(t + Offsets.TERRITORY_NAME)).Trim(),
                        OwnedBy:       ownedBy ?? "",
                        IsCapital:     isCapital,
                        IsPortal:      portalState != 0,
                        IsActivePortal: portalState == 2,
                        Units:         units
                    ));
                }
            }
        }

        return new FullMemorySnapshot
        {
            Players = players, State = state, Round = round,
            CurrentPlayerName = currentPlayerName, CurrentPlayerId = currentPlayerId,
            GameId = gameId, MapState = mapState
        };
    }

    static string GameModeString(int v) => v switch
    {
        0 => "WorldDomination", 1 => "RapidRound",  2 => "RapidPercentage",
        3 => "ZombieApocalypse", 4 => "CapitalConquest", 5 => "Speedy",
        6 => "SecretMission",  7 => "CaptureTheFlag", 8 => "KingOfTheHill",
        9 => "Assassin",       _ => $"Unknown{v}"
    };

    static string CardModeString(int v) => v switch
    {
        0 => "Fixed", 1 => "Progressive", 2 => "Exponential", _ => $"Unknown{v}"
    };
}
