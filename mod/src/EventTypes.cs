namespace StsStats;

/// <summary>
/// docs/API.md の POST /sessions/{id}/events 各要素に対応する不変レコード。
/// EventBuffer から HttpSender / StatsLogger / PayloadJson に渡される。
/// </summary>
internal record EventRecord(
    System.Guid     EventUuid,
    string          EventType,
    System.DateTime OccurredAt,
    string?         PlayerId,
    int?            Floor,
    object          Payload
);
