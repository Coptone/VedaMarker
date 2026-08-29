using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using VedaMarker.Core;

namespace VedaMarker;

internal sealed class WorldTelegraphRenderer(IGameGui gameGui)
{
    private const int CircleSegments = 64;
    private const int ConeSegments = 32;
    private const float GroundOffset = 0.05f;

    private static uint StationFill => ImGui.GetColorU32(new Vector4(0.15f, 0.65f, 1f, 0.22f));
    private static uint StationEdge => ImGui.GetColorU32(new Vector4(0.35f, 0.85f, 1f, 0.95f));
    private static uint CircleFill => ImGui.GetColorU32(new Vector4(1f, 0.15f, 0.1f, 0.20f));
    private static uint CircleEdge => ImGui.GetColorU32(new Vector4(1f, 0.3f, 0.2f, 0.95f));
    private static uint ConeFill => ImGui.GetColorU32(new Vector4(1f, 0.55f, 0.05f, 0.20f));
    private static uint ConeEdge => ImGui.GetColorU32(new Vector4(1f, 0.7f, 0.15f, 0.95f));
    private static uint GuideColor => ImGui.GetColorU32(new Vector4(0.25f, 1f, 0.75f, 0.95f));
    private static uint TextColor => ImGui.GetColorU32(new Vector4(1f, 1f, 1f, 1f));

    public void Draw(Vector3 center, ForsakenTelegraphPlan plan)
    {
        var drawList = ImGui.GetBackgroundDrawList();
        foreach (var telegraph in plan.Telegraphs)
        {
            switch (telegraph.Kind)
            {
                case ForsakenTelegraphKind.Circle:
                    DrawCircle(drawList, center, telegraph.Origin, telegraph.Range, CircleFill, CircleEdge);
                    DrawLabel(
                        drawList,
                        center,
                        telegraph.Origin + new Vector2(0f, -telegraph.Range),
                        telegraph.Label);
                    break;
                case ForsakenTelegraphKind.Cone:
                    DrawCone(drawList, center, telegraph);
                    DrawGuide(drawList, center, telegraph.Origin, telegraph.Target);
                    var direction = Vector2.Normalize(telegraph.Target - telegraph.Origin);
                    DrawLabel(
                        drawList,
                        center,
                        telegraph.Origin + (direction * telegraph.Range * 0.45f),
                        telegraph.Label);
                    break;
            }
        }

        foreach (var station in plan.Stations)
        {
            DrawCircle(drawList, center, station.Position, 0.55f, StationFill, StationEdge);
            DrawLabel(drawList, center, station.Position, station.Label);
        }

        DrawCircle(drawList, center, Vector2.Zero, 0.35f, StationFill, GuideColor);
        DrawLabel(drawList, center, Vector2.Zero, $"模拟中心  W{plan.Wave} / D{plan.Direction8}");
    }

    private void DrawCone(ImDrawListPtr drawList, Vector3 center, ForsakenTelegraph telegraph)
    {
        var direction = telegraph.Target - telegraph.Origin;
        if (direction.LengthSquared() < 0.0001f)
        {
            return;
        }

        var middleAngle = MathF.Atan2(direction.Y, direction.X);
        var halfAngle = telegraph.AngleDegrees * MathF.PI / 360f;
        var originWorld = ToWorld(center, telegraph.Origin);
        var hasOrigin = gameGui.WorldToScreen(originWorld, out var originScreen);
        Vector2? previousScreen = null;
        for (var index = 0; index <= ConeSegments; index++)
        {
            var angle = middleAngle - halfAngle + ((halfAngle * 2f * index) / ConeSegments);
            var edge = telegraph.Origin + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * telegraph.Range;
            var hasEdge = gameGui.WorldToScreen(ToWorld(center, edge), out var edgeScreen);
            if (previousScreen is { } previous && hasEdge)
            {
                if (hasOrigin)
                {
                    drawList.AddTriangleFilled(originScreen, previous, edgeScreen, ConeFill);
                }

                drawList.AddLine(previous, edgeScreen, ConeEdge, 2.5f);
            }

            previousScreen = hasEdge ? edgeScreen : null;
        }

        var start = telegraph.Origin + new Vector2(
            MathF.Cos(middleAngle - halfAngle),
            MathF.Sin(middleAngle - halfAngle)) * telegraph.Range;
        var end = telegraph.Origin + new Vector2(
            MathF.Cos(middleAngle + halfAngle),
            MathF.Sin(middleAngle + halfAngle)) * telegraph.Range;
        if (hasOrigin && gameGui.WorldToScreen(ToWorld(center, start), out var startScreen))
        {
            drawList.AddLine(originScreen, startScreen, ConeEdge, 2.5f);
        }

        if (hasOrigin && gameGui.WorldToScreen(ToWorld(center, end), out var endScreen))
        {
            drawList.AddLine(originScreen, endScreen, ConeEdge, 2.5f);
        }
    }

    private void DrawCircle(
        ImDrawListPtr drawList,
        Vector3 center,
        Vector2 relativeCenter,
        float radius,
        uint fillColor,
        uint edgeColor)
    {
        var worldCenter = ToWorld(center, relativeCenter);
        var hasCenter = gameGui.WorldToScreen(worldCenter, out var centerScreen);
        Vector2? previousScreen = null;
        for (var index = 0; index <= CircleSegments; index++)
        {
            var angle = MathF.Tau * index / CircleSegments;
            var edge = relativeCenter + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
            var hasEdge = gameGui.WorldToScreen(ToWorld(center, edge), out var edgeScreen);
            if (previousScreen is { } previous && hasEdge)
            {
                if (hasCenter)
                {
                    drawList.AddTriangleFilled(centerScreen, previous, edgeScreen, fillColor);
                }

                drawList.AddLine(previous, edgeScreen, edgeColor, 2.5f);
            }

            previousScreen = hasEdge ? edgeScreen : null;
        }
    }

    private void DrawGuide(ImDrawListPtr drawList, Vector3 center, Vector2 origin, Vector2 target)
    {
        if (gameGui.WorldToScreen(ToWorld(center, origin), out var originScreen)
            && gameGui.WorldToScreen(ToWorld(center, target), out var targetScreen))
        {
            drawList.AddLine(originScreen, targetScreen, GuideColor, 3f);
        }
    }

    private void DrawLabel(ImDrawListPtr drawList, Vector3 center, Vector2 position, string label)
    {
        if (!gameGui.WorldToScreen(ToWorld(center, position), out var screen))
        {
            return;
        }

        var size = ImGui.CalcTextSize(label);
        drawList.AddText(screen - (size / 2f), TextColor, label);
    }

    private static Vector3 ToWorld(Vector3 center, Vector2 relative) =>
        new(center.X + relative.X, center.Y + GroundOffset, center.Z + relative.Y);
}
