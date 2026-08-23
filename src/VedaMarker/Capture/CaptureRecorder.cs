using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VedaMarker.Capture;

internal sealed class CaptureRecorder : IDisposable
{
    private const int SchemaVersion = 1;
    private readonly object sync = new();
    private readonly string captureRoot;
    private readonly JsonSerializerOptions jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };
    private readonly Dictionary<uint, string> aliases = [];
    private readonly Dictionary<string, (uint StatusId, uint Param)> activeStatuses = [];
    private readonly Dictionary<string, uint> activeCasts = [];
    private StreamWriter? eventWriter;
    private DateTimeOffset startedAtUtc;
    private string? sessionDirectory;
    private string sessionId = string.Empty;
    private string pluginVersion = string.Empty;
    private string lastPartySignature = string.Empty;
    private uint territoryId;
    private bool? lastCombatState;
    private int nextNonPartyAlias = 1;
    private long sequence;

    public CaptureRecorder(string configDirectory)
    {
        captureRoot = Path.Combine(configDirectory, "captures");
        Directory.CreateDirectory(captureRoot);
    }

    public bool IsActive => eventWriter is not null;

    public long EventCount => sequence;

    public string? LastExportPath { get; private set; }

    public void Start(uint currentTerritoryId, string currentPluginVersion)
    {
        lock (sync)
        {
            if (IsActive)
            {
                throw new InvalidOperationException("A capture session is already active.");
            }

            startedAtUtc = DateTimeOffset.UtcNow;
            sessionId = Guid.NewGuid().ToString("N");
            pluginVersion = currentPluginVersion;
            territoryId = currentTerritoryId;
            sequence = 0;
            aliases.Clear();
            activeStatuses.Clear();
            activeCasts.Clear();
            lastPartySignature = string.Empty;
            lastCombatState = null;
            nextNonPartyAlias = 1;
            LastExportPath = null;

            var folderName = $"VedaMarker-capture-{startedAtUtc:yyyyMMdd-HHmmss}-{sessionId[..8]}";
            sessionDirectory = Path.Combine(captureRoot, folderName);
            Directory.CreateDirectory(sessionDirectory);
            eventWriter = new StreamWriter(
                Path.Combine(sessionDirectory, "events.jsonl"),
                false,
                new UTF8Encoding(false));
            WriteManifest(null, "active");
            RecordLocked("capture_started", new
            {
                polling = "party/status/cast/action-effect",
                privacy = "session-local aliases; no names/content IDs/worlds/chat",
            });
        }
    }

    public string StopAndExport(string reason)
    {
        lock (sync)
        {
            if (!IsActive || sessionDirectory is null)
            {
                throw new InvalidOperationException("No capture session is active.");
            }

            RecordLocked("capture_stopped", new { reason });
            eventWriter!.Flush();
            eventWriter.Dispose();
            eventWriter = null;
            WriteManifest(DateTimeOffset.UtcNow, reason);

            var zipPath = sessionDirectory + ".zip";
            using (var zipStream = new FileStream(zipPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                AddToArchive(archive, Path.Combine(sessionDirectory, "manifest.json"), "manifest.json");
                AddToArchive(archive, Path.Combine(sessionDirectory, "events.jsonl"), "events.jsonl");
            }

            LastExportPath = zipPath;
            sessionDirectory = null;
            return zipPath;
        }
    }

    public void Observe(
        uint currentTerritoryId,
        bool inCombat,
        IReadOnlyList<CapturePartyMember> party,
        IReadOnlyList<CaptureStatusObservation> statuses,
        IReadOnlyList<CaptureCastObservation> casts)
    {
        lock (sync)
        {
            if (!IsActive)
            {
                return;
            }

            if (territoryId != currentTerritoryId)
            {
                var previous = territoryId;
                territoryId = currentTerritoryId;
                RecordLocked("territory_changed", new { previousTerritoryId = previous, territoryId });
            }

            if (lastCombatState != inCombat)
            {
                lastCombatState = inCombat;
                RecordLocked("combat_state_changed", new { inCombat });
            }

            RegisterPartyAliases(party);
            ObserveParty(party);
            ObserveStatuses(statuses);
            ObserveCasts(casts);
        }
    }

    public void RecordActionEffect(uint casterEntityId, uint actionId)
    {
        lock (sync)
        {
            if (!IsActive)
            {
                return;
            }

            RecordLocked("action_effect", new
            {
                actor = AliasFor(casterEntityId),
                actionId,
            });
        }
    }

    public void RecordLifecycle(string eventType)
    {
        lock (sync)
        {
            if (IsActive)
            {
                RecordLocked(eventType, new { });
            }
        }
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (IsActive)
            {
                StopAndExport("plugin_disposed");
            }
        }
    }

    private void RegisterPartyAliases(IEnumerable<CapturePartyMember> party)
    {
        foreach (var member in party)
        {
            aliases[member.EntityId] = $"P{member.PartyIndex + 1}";
        }
    }

    private void ObserveParty(IReadOnlyList<CapturePartyMember> party)
    {
        var signature = string.Join(
            '|',
            party.OrderBy(member => member.PartyIndex).Select(member =>
                $"{member.PartyIndex}:{member.JobId}:{member.InferredRole}"));
        if (signature == lastPartySignature)
        {
            return;
        }

        lastPartySignature = signature;
        RecordLocked("party_snapshot", party.OrderBy(member => member.PartyIndex).Select(member => new
        {
            actor = AliasFor(member.EntityId),
            member.PartyIndex,
            member.JobId,
            role = member.InferredRole?.ToString(),
        }).ToArray());
    }

    private void ObserveStatuses(IReadOnlyList<CaptureStatusObservation> statuses)
    {
        var visible = new Dictionary<string, (uint StatusId, uint Param, float RemainingTime)>();
        foreach (var status in statuses)
        {
            var actor = AliasFor(status.ActorEntityId);
            var key = $"{actor}:{status.StatusId}:{status.Param}";
            visible[key] = (status.StatusId, status.Param, status.RemainingTime);
            if (!activeStatuses.ContainsKey(key))
            {
                RecordLocked("status_added", new
                {
                    actor,
                    status.StatusId,
                    status.Param,
                    remainingTime = Math.Round(status.RemainingTime, 3),
                });
            }
        }

        foreach (var stale in activeStatuses.Keys.Except(visible.Keys).ToArray())
        {
            var separator = stale.IndexOf(':');
            RecordLocked("status_removed", new
            {
                actor = stale[..separator],
                activeStatuses[stale].StatusId,
                activeStatuses[stale].Param,
            });
        }

        activeStatuses.Clear();
        foreach (var entry in visible)
        {
            activeStatuses[entry.Key] = (entry.Value.StatusId, entry.Value.Param);
        }
    }

    private void ObserveCasts(IReadOnlyList<CaptureCastObservation> casts)
    {
        var visible = new Dictionary<string, uint>();
        foreach (var cast in casts)
        {
            var actor = AliasFor(cast.ActorEntityId);
            var key = $"{actor}:{cast.ActionId}";
            visible[key] = cast.ActionId;
            if (!activeCasts.ContainsKey(key))
            {
                RecordLocked("cast_started", new
                {
                    actor,
                    cast.ActionId,
                    currentCastTime = Math.Round(cast.CurrentCastTime, 3),
                });
            }
        }

        foreach (var stale in activeCasts.Keys.Except(visible.Keys).ToArray())
        {
            var separator = stale.IndexOf(':');
            RecordLocked("cast_ended", new
            {
                actor = stale[..separator],
                actionId = activeCasts[stale],
            });
        }

        activeCasts.Clear();
        foreach (var entry in visible)
        {
            activeCasts[entry.Key] = entry.Value;
        }
    }

    private string AliasFor(uint entityId)
    {
        if (aliases.TryGetValue(entityId, out var alias))
        {
            return alias;
        }

        alias = $"N{nextNonPartyAlias++}";
        aliases[entityId] = alias;
        return alias;
    }

    private void RecordLocked(string eventType, object data)
    {
        var observedAtUtc = DateTimeOffset.UtcNow;
        var envelope = new CaptureEnvelope(
            SchemaVersion,
            sessionId,
            ++sequence,
            observedAtUtc,
            (long)(observedAtUtc - startedAtUtc).TotalMilliseconds,
            territoryId,
            eventType,
            data);
        eventWriter!.WriteLine(JsonSerializer.Serialize(envelope, jsonOptions));
        eventWriter.Flush();
    }

    private void WriteManifest(DateTimeOffset? endedAtUtc, string stopReason)
    {
        var manifest = new CaptureManifest(
            SchemaVersion,
            sessionId,
            pluginVersion,
            startedAtUtc,
            endedAtUtc,
            sequence,
            "No character names, account/Content IDs, world names, chat, or credentials.",
            stopReason);
        var formattedOptions = new JsonSerializerOptions(jsonOptions) { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(sessionDirectory!, "manifest.json"),
            JsonSerializer.Serialize(manifest, formattedOptions),
            new UTF8Encoding(false));
    }

    private static void AddToArchive(ZipArchive archive, string sourcePath, string entryName)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var input = File.OpenRead(sourcePath);
        using var output = entry.Open();
        input.CopyTo(output);
    }
}
