using VedaMarker.Core;

namespace VedaMarker.Capture;

internal sealed record CapturePartyMember(
    int PartyIndex,
    uint EntityId,
    uint JobId,
    RoleSlot? InferredRole);

internal sealed record CaptureStatusObservation(
    uint ActorEntityId,
    uint StatusId,
    uint Param,
    float RemainingTime);

internal sealed record CaptureCastObservation(
    uint ActorEntityId,
    uint ActionId,
    float CurrentCastTime);

internal sealed record CaptureManifest(
    int SchemaVersion,
    string SessionId,
    string PluginVersion,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset? EndedAtUtc,
    long EventCount,
    string Privacy,
    string StopReason);

internal sealed record CaptureEnvelope(
    int SchemaVersion,
    string SessionId,
    long Sequence,
    DateTimeOffset ObservedAtUtc,
    long ElapsedMs,
    uint TerritoryId,
    string EventType,
    object Data);
