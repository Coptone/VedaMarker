using System.Runtime.InteropServices;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed class NativeMarkerProvider : IMarkerProvider
{
    private const string MarkingFunctionSignature =
        "48 89 5C 24 10 48 89 6C 24 18 57 48 83 EC 20 8D 42";
    private const int MarkerPhaseTransitionDelayMs = 750;

    private static readonly IReadOnlyDictionary<PartyMarker, MarkerIndex> MarkerIndices =
        new Dictionary<PartyMarker, MarkerIndex>
        {
            [PartyMarker.Attack1] = new(0, 1),
            [PartyMarker.Attack2] = new(1, 2),
            [PartyMarker.Attack3] = new(2, 3),
            [PartyMarker.Attack4] = new(3, 4),
            [PartyMarker.Bind1] = new(5, 6),
            [PartyMarker.Bind2] = new(6, 7),
            [PartyMarker.Ignore1] = new(8, 9),
            [PartyMarker.Ignore2] = new(9, 10),
        };

    private readonly Func<int> intervalMilliseconds;
    private readonly Func<uint?> resolveLocalActorId;
    private readonly Func<int, uint?> resolvePartySlotActorId;
    private readonly Queue<MarkerOperation> pendingOperations = new();
    private MarkingFunctionDelegate? markingFunction;
    private IReadOnlyList<uint> lastTargetActorIds = Array.Empty<uint>();
    private long lastOperationAt;
    private bool? lastOperationWasClear;
    private bool hasSubmittedMarkers;
    private bool cleanupScheduled;

    public NativeMarkerProvider(
        ISigScanner sigScanner,
        Func<int> intervalMilliseconds,
        Func<uint?> resolveLocalActorId,
        Func<int, uint?> resolvePartySlotActorId)
    {
        this.intervalMilliseconds = intervalMilliseconds;
        this.resolveLocalActorId = resolveLocalActorId;
        this.resolvePartySlotActorId = resolvePartySlotActorId;

        try
        {
            if (!sigScanner.TryScanText(MarkingFunctionSignature, out var address)
                || address == nint.Zero)
            {
                AvailabilityStatus = "当前游戏版本未找到原生标点函数；真实标点已拒绝启用";
                return;
            }

            markingFunction = Marshal.GetDelegateForFunctionPointer<MarkingFunctionDelegate>(address);
            IsAvailable = true;
            AvailabilityStatus = "原生标点函数已就绪";
        }
        catch (Exception exception)
        {
            AvailabilityStatus = $"原生标点初始化失败：{exception.Message}";
        }
    }

    public string Name => "可选目标原生团队标点（实验）";

    public bool ProducesGameMarkers => true;

    public int PendingCommandCount => pendingOperations.Count;

    public bool IsAvailable { get; }

    public string AvailabilityStatus { get; }

    public int SubmittedOperationCount { get; private set; }

    public string? LastOperation { get; private set; }

    public void Submit(
        ValidatedMarkerAssignment assignment,
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        EnsureAvailable();
        var targets = PartyMarkerSubmissionValidator.Validate(
            assignment,
            targetRoles,
            localRole,
            partySlots);
        var resolvedTargets = targets
            .Select(role => new ResolvedTarget(role, ResolveActorId(role, localRole, partySlots)))
            .ToArray();

        pendingOperations.Clear();
        cleanupScheduled = false;
        foreach (var target in resolvedTargets)
        {
            pendingOperations.Enqueue(MarkerOperation.Clear(target.ActorId));
        }

        foreach (var target in resolvedTargets)
        {
            pendingOperations.Enqueue(MarkerOperation.Apply(target.ActorId, assignment.Markers[target.Role]));
        }

        lastTargetActorIds = resolvedTargets.Select(target => target.ActorId).Distinct().ToArray();
        hasSubmittedMarkers = true;
    }

    public void SubmitDiagnosticSelfMarker(PartyMarker marker)
    {
        EnsureAvailable();
        var actorId = ResolveLocalActorId();
        pendingOperations.Clear();
        cleanupScheduled = false;
        pendingOperations.Enqueue(MarkerOperation.Clear(actorId));
        pendingOperations.Enqueue(MarkerOperation.Apply(actorId, marker));
        lastTargetActorIds = [actorId];
        hasSubmittedMarkers = true;
    }

    public void SubmitDiagnosticSelfClear()
    {
        EnsureAvailable();
        var actorId = ResolveLocalActorId();
        pendingOperations.Clear();
        cleanupScheduled = true;
        pendingOperations.Enqueue(MarkerOperation.Clear(actorId));
        lastTargetActorIds = [actorId];
        hasSubmittedMarkers = true;
    }

    public void Tick(long now)
    {
        if (pendingOperations.Count == 0)
        {
            return;
        }

        var nextIsClear = pendingOperations.Peek().Marker is null;
        var interval = Math.Clamp(intervalMilliseconds(), 100, 1000);
        if (lastOperationWasClear.HasValue && lastOperationWasClear.Value != nextIsClear)
        {
            interval = Math.Max(interval, MarkerPhaseTransitionDelayMs);
        }

        if (lastOperationAt != 0 && now - lastOperationAt < interval)
        {
            return;
        }

        var operation = pendingOperations.Dequeue();
        Execute(operation);
        lastOperationAt = now;
        lastOperationWasClear = nextIsClear;
        if (pendingOperations.Count == 0 && cleanupScheduled)
        {
            cleanupScheduled = false;
            hasSubmittedMarkers = false;
            lastTargetActorIds = Array.Empty<uint>();
        }
    }

    public void Clear(bool immediate = false)
    {
        pendingOperations.Clear();
        if (!hasSubmittedMarkers && !cleanupScheduled)
        {
            return;
        }

        cleanupScheduled = false;
        if (immediate)
        {
            Exception? firstFailure = null;
            foreach (var actorId in lastTargetActorIds)
            {
                try
                {
                    Execute(MarkerOperation.Clear(actorId));
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }

            ResetCleanupState();
            if (firstFailure is not null)
            {
                throw new InvalidOperationException("插件卸载时未能完成全部原生标点清理。", firstFailure);
            }

            return;
        }

        foreach (var actorId in lastTargetActorIds)
        {
            pendingOperations.Enqueue(MarkerOperation.Clear(actorId));
        }

        cleanupScheduled = true;
    }

    private uint ResolveActorId(
        RoleSlot role,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        if (role == localRole)
        {
            return ResolveLocalActorId();
        }

        if (!partySlots.TryGetValue(role, out var slot))
        {
            throw new MarkerAssignmentException($"无法解析 {role} 的队伍序号。");
        }

        return resolvePartySlotActorId(slot) is { } actorId && actorId != 0
            ? actorId
            : throw new MarkerAssignmentException($"无法解析队伍第 {slot} 位的游戏对象。");
    }

    private uint ResolveLocalActorId() =>
        resolveLocalActorId() is { } actorId && actorId != 0
            ? actorId
            : throw new MarkerAssignmentException("当前无法识别插件使用者本人。");

    private unsafe void Execute(MarkerOperation operation)
    {
        EnsureAvailable();
        var controller = MarkingController.Instance();
        if (controller == null)
        {
            throw new InvalidOperationException("当前无法读取游戏标点控制器。");
        }

        if (operation.Marker is null)
        {
            if (!TryFindCurrentMarker(controller, operation.ActorId, out var currentMarker))
            {
                LastOperation = "原生清除：目标当前无标点";
                return;
            }

            markingFunction!((nint)controller, MarkerIndices[currentMarker].FunctionIndex, operation.ActorId);
            SubmittedOperationCount++;
            LastOperation = $"原生清除：{currentMarker}";
            return;
        }

        var marker = operation.Marker.Value;
        if (TryFindCurrentMarker(controller, operation.ActorId, out var existingMarker)
            && existingMarker == marker)
        {
            LastOperation = $"原生标记：{marker} 已存在";
            return;
        }

        markingFunction!((nint)controller, MarkerIndices[marker].FunctionIndex, operation.ActorId);
        SubmittedOperationCount++;
        LastOperation = $"原生标记：{marker}";
    }

    private static unsafe bool TryFindCurrentMarker(
        MarkingController* controller,
        uint actorId,
        out PartyMarker marker)
    {
        foreach (var entry in MarkerIndices)
        {
            if (controller->Markers[entry.Value.MemoryIndex].ObjectId == actorId)
            {
                marker = entry.Key;
                return true;
            }
        }

        marker = default;
        return false;
    }

    private void EnsureAvailable()
    {
        if (!IsAvailable || markingFunction is null)
        {
            throw new InvalidOperationException(AvailabilityStatus);
        }
    }

    private void ResetCleanupState()
    {
        lastOperationAt = 0;
        lastOperationWasClear = null;
        hasSubmittedMarkers = false;
        cleanupScheduled = false;
        lastTargetActorIds = Array.Empty<uint>();
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate char MarkingFunctionDelegate(nint controller, byte markerIndex, uint actorId);

    private readonly record struct MarkerIndex(int MemoryIndex, byte FunctionIndex);

    private readonly record struct ResolvedTarget(RoleSlot Role, uint ActorId);

    private readonly record struct MarkerOperation(uint ActorId, PartyMarker? Marker)
    {
        public static MarkerOperation Clear(uint actorId) => new(actorId, null);

        public static MarkerOperation Apply(uint actorId, PartyMarker marker) => new(actorId, marker);
    }
}
