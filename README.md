# Risk Game Recorder

## Important note

The recording is in a form of a JSON file. It's quite human readable, but no nice user interface for replaying exists yet. Perhaps next weekend.

While the source code is (almost) entirely available, the offsets have been removed. They will not be provided. The binary itself is obfuscated as well, to prevent trivial reverse engineering of offsets.

## How it works

Every 50ms the game state is being read and compared with the previous state. If there's a change, the delta is recorded.

## Download

Grab the latest Release from [GitHub Releases](https://github.com/svengjohnson/RiskGameRecorder/releases)

## How do I use this?

Make sure it's launched before the Risk game starts. And keep it running until the conclusion of the game, otherwise the replay won't be saved.

## What's recorded

- Territory changes (including the ones behind the fog)
- Alliances
- Player cards (after the game, you can check whether they had that trade on 3 they have been holding!)
- Card Trades
- Player kills

## What's not recorded

- The territory against a which roll completely failed (lost 0), as that doesn't change the map state for the attacked territory.
- There's no distinction between a blitz and a slow roll, if <3 lost/killed
- Territories highlighted/targetted
- Emotes

## User Interface

<img width="342" height="475" alt="image" src="https://github.com/user-attachments/assets/4332d001-d2d4-4c9b-8741-d11608479875" />


## Reading the recording file

[Example Recording JSON](https://github.com/svengjohnson/RiskGameRecorder/blob/main/2026-03-11--06-43-Alcatraz-CapitalConquest-Exponential-v1.json)

[Viewer - currently need to build & run locally, but I'll host it soon](https://github.com/svengjohnson/RiskGameReplayViewer)

### metadata & gameInfo

```
"metadata": {
  "version": 1,
  "date": "2026-03-09 11:44:55 +02:00" // local time + UTC offset when the game started
},
"gameInfo": {
  "id": "925d0878-5e3e-455e-b5a1-6147b6339cd3",
  "isSoloGame": false, // true for bot games
  "map": "Alcatraz",
  "alliances": true,
  "fog": true,
  "blizzards": true,
  "gameMode": "CapitalConquest",
  "cardType": "Exponential",
  "dice": "BalancedBlitz",
  "inactivityBehavior": "Neutral",
  "portals": "Off",
  "localPlayer" : 1, // the player actually playing the game - will match the dict key of the players{} below.
  "gameDuration": 1229300 // how long the game took, in milliseconds (rounded to 50ms)
},
```

### players

Players are keyed by their sequential turn order ID (1 = goes first, 2 = goes second, etc.).

```
"players": {
  "1": {
    "lobbyIndex": 0, // lobbyIndex will always be 0 for the host
    "userId": 48912894,
    "deviceId": "0f4ee81d5eb3bef8377ac6a4881be897",
    "name": "SJohnson",
    "colour": "color_white",
    "rank": "GrandMaster",
    "rank1v1": "Intermediate",
    "battlePoints": 189450,
    "isBotted": false // currently doesn't work but will be true for players that started as bots first
  },
  "2": {
    "lobbyIndex": 5,
    "userId": 54535920,
    "deviceId": "5a237b9277fe3452361a3c3526be4d6b",
    "name": "General Gibs-A-Lot 27388",
    "colour": "color_royale", // purple :)
    "rank": "Novice",
    "rank1v1": "Novice",
    "battlePoints": 22760,
    "isBotted": false
  },
  ...
},
```

```
"blizzards": [
  "Overseers Mess",
  "Screen Room"
],
```

### Actual game state recording

Rounds are keyed by their round number. Round `0` is the capital selection phase in Capital Conquest.

```
"roundInfo": {
  "0": {
    "mapState": { // full map state at the start of the round, keyed by territory name
      "Hospital": {
        "ownedBy": 6, // which player owns it (matches the key in players{})
        "isCapital": false,
        "isPortal": false,
        "isActivePortal": false,
        "units": 3
      },
      ...
    },
    "players": { // player status at the START of this round
      "1": {
        "isDead": false,
        "isTakenOverByAI": false,
        "isBotFlagged": false,
        "isQuit": false,
        "territories": 7,
        "capitals": 1,
        "units": 23,
        "cards": []
      },
      ...
    },
    "alliances": { // alliance state at the START of this round
      "1": [],
      "2": [],
      "3": [],
      "4": [],
      "5": [],
      "6": []
    },
    "playerTurns": {
      "1": {
        "income": 3, // sanity check — may not be accurate in Sandbox games
        "territories": 7, // how many territories at the start of their turn
        "capitals": 0,
        "units": 20,
        "cardsAtTurnStart": [], // cards held when their turn began
        "snapshots": [ // actual changes during this player's turn
          {
            "type": "territory",
            "territories": { // round 0 of a capitals game — capital selection
              "Second Floor Hall": {
                "ownedBy": 1,
                "isCapital": true,
                "isPortal": false,
                "isActivePortal": false,
                "units": 9,
                "previousUnits": 6
              }
            },
            "time": 31350 // ms since game start, rounded to nearest 50ms
          }
        ],
        "cardsAfterTurn": []
      },
      ...
      "4": {
        "income": 3,
        "territories": 6,
        "capitals": 0,
        "units": 20,
        "cardsAtTurnStart": ["A", "A", "Wild"],
        "snapshots": [
          {
            "type": "alliance", // during player 4's turn, player 2 and 6 established an alliance
            "alliances": {
              "1": [], "2": [6], "3": [], "4": [], "5": [], "6": [2]
            },
            "time": 45250
          },
          {
            "type": "cards_traded", // player traded in 3 cards
            "cards": ["A", "A", "Wild"], // the cards that were traded in
            "time": 55000
          },
          {
            "type": "territory",
            "territories": {
              "Pantry": {
                "ownedBy": 4,
                "isCapital": true,
                "isPortal": false,
                "isActivePortal": false,
                "units": 6,
                "previousUnits": 3
              }
            },
            "time": 58100
          },
          {
            "type": "player_killed", // player 5 was eliminated this turn
            "player": {
              "id": 5,       // the eliminated player
              "killedBy": 4, // the player who took their last territory
              "cards": ["B"] // cards the killed player held (now transferred to killer)
            },
            "time": 58100
          }
        ],
        "cardsAfterTurn": ["B", "B", "B"]
      }
    }
  }
}
```

### Snapshot of a territory changing ownership

From this we can infer that Player 1 attacked Player 3, from Passage → Lecher Room. Lost nothing.

```
{
  "type": "territory",
  "territories": {
    "Lecher Room": {
      "ownedBy": 1,
      "isCapital": false,
      "isPortal": false,
      "isActivePortal": false,
      "units": 45,
      "previouslyOwnedBy": 3,
      "previousUnits": 1
    },
    "Passage": {
      "ownedBy": 1,
      "isCapital": true,
      "isPortal": false,
      "isActivePortal": false,
      "units": 1,
      "previousUnits": 46
    }
  },
  "time": 1191300
}
```

### game_over snapshot

Appended to the active player's turn when the game ends.

```
{
  "type": "game_over",
  "time": 1229300
}
```
