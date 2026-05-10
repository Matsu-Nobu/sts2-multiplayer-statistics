namespace StsStats;

/// <summary>
/// docs/api.md の POST /sessions/{id}/events 各要素に対応する不変レコード。
/// EventBuffer から HttpSender / StatsLogger / PayloadJson に渡される。
///
/// 戦闘内 event は CombatIndex / TurnNumber / Sequence を set、戦闘外は null。
/// </summary>
internal record EventRecord(
    System.Guid     EventUuid,
    string          EventType,
    System.DateTime OccurredAt,
    string?         PlayerId,
    int?            Floor,
    int?            CombatIndex,
    int?            TurnNumber,
    int?            Sequence,
    object          Payload
);
