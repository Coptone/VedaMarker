using VedaMarker.Core;

namespace VedaMarker.Capture;

internal sealed record CapturePosition(float X, float Y, float Z);

internal sealed record CaptureActionMetadata(
    string? Name,
    uint? CastType,
    uint? EffectRange,
    uint? XAxisModifier);

internal sealed record CapturePartyMember(
    int PartyIndex,
    uint EntityId,
    uint JobId,
    RoleSlot? InferredRole,
    CapturePosition? Position = null,
    float Rotation = 0,
    float HitboxRadius = 0,
    bool IsDead = false);

internal sealed record CaptureStatusObservation(
    uint ActorEntityId,
    uint StatusId,
    uint Param,
    float RemainingTime);

internal sealed record CaptureCastObservation(
    uint ActorEntityId,
    uint ActionId,
    CaptureActionMetadata Action,
    float CurrentCastTime,
    float TotalCastTime,
    CapturePosition SourcePosition,
    float SourceRotation,
    float SourceHitboxRadius,
    uint? TargetEntityId,
    CapturePosition? TargetPosition,
    CapturePosition? TargetLocation,
    float? CastRotation);

internal sealed record CaptureActionTarget(
    uint EntityId,
    CapturePosition? Position);

internal sealed record CaptureObjectObservation(
    int ObjectIndex,
    uint EntityId,
    uint BaseId,
    string ObjectKind,
    CapturePosition Position,
    float Rotation,
    float HitboxRadius,
    bool IsDead);

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
