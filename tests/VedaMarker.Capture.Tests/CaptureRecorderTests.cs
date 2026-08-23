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
                [new CapturePartyMember(0, 987_654_321, JobCatalog.Warrior, RoleSlot.MT)],
                [new CaptureStatusObservation(987_654_321, 4_242, 7, 12.5f)],
                [new CaptureCastObservation(123_456_789, 8_484, 1.25f)]);
            recorder.RecordActionEffect(123_456_789, 8_484);

            var zipPath = recorder.StopAndExport("test_complete");

            Assert.True(File.Exists(zipPath));
            using var archive = ZipFile.OpenRead(zipPath);
            Assert.Equal(["events.jsonl", "manifest.json"],
                archive.Entries.Select(entry => entry.FullName).OrderBy(name => name).ToArray());
            var events = ReadEntry(archive, "events.jsonl");
            var manifest = ReadEntry(archive, "manifest.json");
            Assert.Contains("\"schemaVersion\":1", events);
            Assert.Contains("\"actor\":\"P1\"", events);
            Assert.Contains("\"actor\":\"N1\"", events);
            Assert.Contains("\"statusId\":4242", events);
            Assert.Contains("\"actionId\":8484", events);
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
