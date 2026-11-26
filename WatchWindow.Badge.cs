using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace PartyFinderWatch;

internal sealed partial class WatchWindow
{
    private const string BadgeLabel = "Party Finder Watch";
    private static readonly Vector4 AlertGold = new(1f, 0.67f, 0.27f, 1f);
    private bool draggingBadge;

    private void DrawBadge()
    {
        var viewport = ImGui.GetMainViewport();
        var defaultPosition = viewport.WorkPos + new Vector2(280, 75);
        var savedPosition = new Vector2(plugin.Config.IconX ?? defaultPosition.X, plugin.Config.IconY ?? defaultPosition.Y);
        var labelSize = ImGui.CalcTextSize(BadgeLabel);
        float scale = Math.Clamp(ImGui.GetFontSize() / 16f, 0.9f, 2.5f);
        var size = new Vector2(labelSize.X + 72 * scale, Math.Max(42 * scale, labelSize.Y + 16 * scale));
        var maximum = Vector2.Max(viewport.WorkPos, viewport.WorkPos + viewport.WorkSize - size - new Vector2(10));
        var position = Vector2.Clamp(savedPosition, viewport.WorkPos, maximum);
        ImGui.SetNextWindowPos(position, ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(5));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10 * scale);

        const ImGuiWindowFlags flags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoBackground;
        if (ImGui.Begin("##pfwatch-icon", flags))
        {
            int unread = plugin.UnreadCount;
            float pulse = plugin.IsFlashing ? 0.55f + 0.45f * (float)Math.Cos(ImGui.GetTime() * 9) : 0.72f;
            ImGui.PushStyleColor(ImGuiCol.Button, unread > 0 ? Vector4.Lerp(Panel, Selected, pulse) : Panel);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Hover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, Selected);
            bool clicked = ImGui.Button("##open-party-finder-watch", size);
            ImGui.PopStyleColor(3);
            PaintBadge(labelSize, scale, unread > 0, pulse);

            if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            {
                draggingBadge = true;
                var movedPosition = Vector2.Clamp(position + ImGui.GetIO().MouseDelta, viewport.WorkPos, maximum);
                plugin.Config.IconX = movedPosition.X;
                plugin.Config.IconY = movedPosition.Y;
            }

            if (draggingBadge && ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                plugin.Save();
                draggingBadge = false;
            }
            else if (clicked && !draggingBadge)
            {
                Open();
            }

            if (ImGui.IsItemHovered() && !draggingBadge)
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Data center  {plugin.DataCenter}");
                if (unread > 0)
                    ImGui.TextUnformatted(unread == 1 ? "1 new match" : $"{unread} new matches");
                ImGui.TextDisabled("Click to open · Drag to move");
                ImGui.EndTooltip();
            }
        }
        ImGui.End();
        ImGui.PopStyleVar(2);
    }

    private static void PaintBadge(Vector2 labelSize, float scale, bool alert, float pulse)
    {
        var draw = ImGui.GetWindowDrawList();
        var topLeft = ImGui.GetItemRectMin();
        var bottomRight = ImGui.GetItemRectMax();
        float middleY = (topLeft.Y + bottomRight.Y) * 0.5f;
        var border = Vector4.Lerp(Line, alert ? AlertGold : Accent, alert ? 0.42f + 0.58f * pulse : 0.40f);
        draw.AddRect(topLeft, bottomRight, ImGui.GetColorU32(border), 10 * scale, ImDrawFlags.None, 1.5f * scale);

        var bellCenter = new Vector2(topLeft.X + 21 * scale, middleY);
        var tile = new Vector2(14 * scale);
        draw.AddRectFilled(bellCenter - tile, bellCenter + tile, ImGui.GetColorU32(Accent), 7 * scale);
        DrawBell(draw, bellCenter, scale);
        draw.AddText(new Vector2(topLeft.X + 44 * scale, middleY - labelSize.Y * 0.5f), ImGui.GetColorU32(Ink), BadgeLabel);

        var statusCenter = new Vector2(bottomRight.X - 14 * scale, middleY);
        if (alert)
        {
            draw.AddCircleFilled(statusCenter, 8.5f * scale, ImGui.GetColorU32(AlertGold));
            draw.AddText(statusCenter - ImGui.CalcTextSize("!") * 0.5f, ImGui.GetColorU32(AccentInk), "!");
        }
        else
        {
            uint gripColor = ImGui.GetColorU32(Muted);
            for (int row = -1; row <= 1; row++)
            {
                float y = row * 4 * scale;
                draw.AddCircleFilled(statusCenter + new Vector2(-2 * scale, y), 1.15f * scale, gripColor);
                draw.AddCircleFilled(statusCenter + new Vector2(2 * scale, y), 1.15f * scale, gripColor);
            }
        }
    }

    private static void DrawBell(ImDrawListPtr draw, Vector2 center, float scale)
    {
        Vector2 At(float x, float y) => center + new Vector2(x, y) * scale;
        uint color = ImGui.GetColorU32(AccentInk);
        float lineWidth = 1.7f * scale;
        draw.PathLineTo(At(-7.4f, 3.4f));
        draw.PathBezierCubicCurveTo(At(-4.5f, 1), At(-6.4f, -6.4f), At(0, -6.4f));
        draw.PathBezierCubicCurveTo(At(6.4f, -6.4f), At(4.5f, 1), At(7.4f, 3.4f));
        draw.PathStroke(color, ImDrawFlags.Closed, lineWidth);
        draw.AddCircleFilled(At(0, 6.2f), 1.25f * scale, color);
        draw.AddLine(At(0, -8.2f), At(0, -6.4f), color, lineWidth);
    }
}
