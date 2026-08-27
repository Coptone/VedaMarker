using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed class ChatCommandMarkerProvider(Func<int> intervalMilliseconds) : IMarkerProvider
{
    private static readonly HashSet<string> AllowedCommands = BuildAllowedCommands();

    private readonly Queue<string> pendingCommands = new();
    private long lastCommandAt;
    private bool hasSubmittedMarkers;
    private bool cleanupScheduled;
    private IReadOnlyList<string> lastClearCommands = Array.Empty<string>();

    public string Name => "可选目标团队标点（实验）";

    public bool ProducesGameMarkers => true;

    public int PendingCommandCount => pendingCommands.Count;

    public void Submit(
        ValidatedMarkerAssignment assignment,
        IReadOnlyCollection<RoleSlot> targetRoles,
        RoleSlot localRole,
        IReadOnlyDictionary<RoleSlot, int> partySlots)
    {
        var commands = PartyMarkerCommandPlanner.BuildAssignmentCommands(
            assignment,
            targetRoles,
            localRole,
            partySlots);
        lastClearCommands = PartyMarkerCommandPlanner.BuildClearCommands(
            targetRoles,
            localRole,
            partySlots);
        pendingCommands.Clear();
        cleanupScheduled = false;
        foreach (var command in commands)
        {
            pendingCommands.Enqueue(command);
        }

        hasSubmittedMarkers = true;
    }

    public void Tick(long now)
    {
        if (pendingCommands.Count == 0)
        {
            return;
        }

        var interval = Math.Clamp(intervalMilliseconds(), 100, 1000);
        if (lastCommandAt != 0 && now - lastCommandAt < interval)
        {
            return;
        }

        lastCommandAt = now;
        ExecuteCommand(pendingCommands.Peek());
        pendingCommands.Dequeue();
        if (pendingCommands.Count == 0 && cleanupScheduled)
        {
            cleanupScheduled = false;
            hasSubmittedMarkers = false;
            lastClearCommands = Array.Empty<string>();
        }
    }

    public void Clear(bool immediate = false)
    {
        pendingCommands.Clear();
        if (!hasSubmittedMarkers && !cleanupScheduled)
        {
            return;
        }

        cleanupScheduled = false;
        var commands = lastClearCommands;
        if (immediate)
        {
            Exception? firstFailure = null;
            foreach (var command in commands)
            {
                try
                {
                    ExecuteCommand(command);
                }
                catch (Exception exception)
                {
                    firstFailure ??= exception;
                }
            }

            lastCommandAt = 0;
            hasSubmittedMarkers = false;
            lastClearCommands = Array.Empty<string>();
            if (firstFailure is not null)
            {
                throw new InvalidOperationException("插件卸载时未能完成全部标点清理命令。", firstFailure);
            }

            return;
        }

        foreach (var command in commands)
        {
            pendingCommands.Enqueue(command);
        }

        cleanupScheduled = true;
    }

    private static unsafe void ExecuteCommand(string command)
    {
        if (!AllowedCommands.Contains(command))
        {
            throw new InvalidOperationException("拒绝执行标点白名单之外的命令。");
        }

        var bytes = Encoding.UTF8.GetBytes(command);
        var message = Utf8String.FromSequence(bytes.Append((byte)0).ToArray());
        try
        {
            UIModule.Instance()->ProcessChatBoxEntry(message);
        }
        finally
        {
            message->Dtor(true);
        }
    }

    private static HashSet<string> BuildAllowedCommands()
    {
        string[] markers = ["off", "attack1", "attack2", "attack3", "attack4", "bind1", "bind2", "stop1", "stop2"];
        string[] targets = ["<me>", "<1>", "<2>", "<3>", "<4>", "<5>", "<6>", "<7>", "<8>"];
        return markers.SelectMany(marker => targets.Select(target => $"/mk {marker} {target}"))
            .ToHashSet(StringComparer.Ordinal);
    }
}
