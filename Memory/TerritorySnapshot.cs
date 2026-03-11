namespace RiskGameRecorder.Memory;

public sealed record TerritorySnapshot(
    string TerritoryName,
    string OwnedBy,
    bool   IsCapital,
    bool   IsPortal,
    bool   IsActivePortal,
    int    Units
);
