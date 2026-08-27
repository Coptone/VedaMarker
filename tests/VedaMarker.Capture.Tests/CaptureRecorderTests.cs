using System.IO.Compression;
using VedaMarker.Core;

namespace VedaMarker.Capture.Tests;

public sealed class CaptureRecorderTests
{
    [Fact]
    public void StopAndExport_WritesVersionedAliasedZipWithoutRawEntityIds()
    {
        var root = Path.Combine(Path.GetTempPath(), $"VedaMarkerCaptureTest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            using var recorder = new CaptureRecorder(root);
            recorder.Start(1_363, "0.1.0-test");
            recorder.Observe(
                1_363,
                true,
                [new CapturePartyMember(
                    0,
                    987_654_321,
                    JobCatalog.Warrior,
                    RoleSlot.MT,
                    new CapturePosition(100, 0, 100),
                    1.25f,
                    2.5f)],
                [new CaptureStatusObservation(987_654_321, 4_242, 7, 12.5f)],
                [new CaptureCastObservation(
                    123_456_789,
                    8_484,
                    new CaptureActionMetadata("测试技能", 2, 40, 8),
                    1.25f,
                    4.5f,
                    new CapturePosition(105, 0, 110),
                    0.5f,
                    3,
                    987_654_321,
                    new CapturePosition(100, 0, 100),
                    new CapturePosition(101, 0, 102),
                    0.75f)],
                [new CaptureObjectObservation(
                    42,
                    123_456_789,
                    8_888,
                    "BattleNpc",
                    new CapturePosition(105, 0, 110),
                    0.5f,
                    3,
                    false)]);
            recorder.RecordActionEffect(
                123_456_789,
                8_484,
                new CaptureActionMetadata("测试技能", 2, 40, 8),
                new CapturePosition(105, 0, 110),
                0.5f,
                new CapturePosition(101, 0, 102),
                0.75f,
                [new CaptureActionTarget(987_654_321, new CapturePosition(100, 0, 100))]);
            recorder.RecordMapEffect(
                12,
                2,
                9,
                [new CaptureObjectObservation(
                    43,
                    0xE0000000,
                    10_000,
                    "EventObj",
                    new CapturePosition(95, 0, 95),
                    0,
                    1,
                    false)]);

            var zipPath = recorder.StopAndExport("test_complete");

            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(["events.jsonl", "manifest.json"],
                archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray());
            var events = ReadEntry(archive, "events.jsonl");
            var manifest = ReadEntry(archive, "manifest.json");
            Assert.Contains("\"schemaVersion\":2", events);
            Assert.Contains("\"actor\":\"P1\"", events);
            Assert.Contains("\"actor\":\"N1\"", events);
            Assert.Contains("\"actor\":\"O43\"", events);
            Assert.Contains("\"statusId\":4242", events);
            Assert.Contains("\"actionId\":8484", events);
            Assert.Contains("\"name\":\"测试技能\"", events);
            Assert.Contains("\"castType\":2", events);
            Assert.Contains("\"targetLocation\":{\"x\":101", events);
            Assert.Contains("\"eventType\":\"position_snapshot\"", events);
            Assert.Contains("\"eventType\":\"map_effect\"", events);
            Assert.DoesNotContain("987654321", events);
            Assert.DoesNotContain("123456789", events);
            Assert.Contains("No character names", manifest);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string ReadEntry(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw new InvalidOperationException($"Missing {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }
}
