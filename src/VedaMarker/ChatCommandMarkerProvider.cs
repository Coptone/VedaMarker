using System.Text;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed class ChatCommandMarkerProvider(Func<int> intervalMilliseconds) : IMarkerProvider
{
    private static readonly HashSet<string> AllowedSelfCommands = new(StringComparer.Ordinal)
    {
        "/mk off <me>",
        "/mk attack1 <me>",
        "/mk attack2 <me>",
        "/mk attack3 <me>",
        "/mk attack4 <me>",
        "/mk bind1 <me>",
        "/mk bind2 <me>",
        "/mk stop1 <me>",
        "/mk stop2 <me>",
    };

    private readonly Queue<string> pendingCommands = new();
    private long lastCommandAt;
    private bool hasSubmittedMarkers;
    private bool cleanupScheduled;

    public string Name => "本人团队标点（实验）";

    public bool ProducesGameMarkers => true;

    public int PendingCommandCount => pendingCommands.Count;

    public void Submit(
        ValidatedMarkerAssignment assignment,
        RoleSlot localRole)
    {
        var commands = PartyMarkerCommandPlanner.BuildSelfAssignmentCommands(assignment, localRole);
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
        var commands = PartyMarkerCommandPlanner.BuildSelfClearCommands();
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
        if (!AllowedSelfCommands.Contains(command))
        {
            throw new InvalidOperationException("拒绝执行本人标点白名单之外的命令。");
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
}
