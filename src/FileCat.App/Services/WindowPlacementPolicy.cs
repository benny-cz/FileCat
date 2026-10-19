using FileCat.Core.State;

namespace FileCat.App.Services;

internal readonly record struct DisplayWorkArea(int X, int Y, int Width, int Height, double Scaling);

/// <summary>Restores a saved normal rectangle in physical screen coordinates and logical window dimensions.</summary>
internal static class WindowPlacementPolicy
{
    public static WindowPlacement? Restore(WindowPlacement? saved, IReadOnlyList<DisplayWorkArea> displays)
    {
        if (saved is null || !double.IsFinite(saved.Width) || !double.IsFinite(saved.Height) ||
            saved.Width < 200 || saved.Height < 150) return null;
        var usable = displays.Where(d => d.Width > 0 && d.Height > 0 &&
            double.IsFinite(d.Scaling) && d.Scaling is >= 0.25 and <= 8).ToArray();
        if (usable.Length == 0) return null;

        bool validPosition = double.IsFinite(saved.X) && double.IsFinite(saved.Y) &&
            saved.X is >= int.MinValue and <= int.MaxValue && saved.Y is >= int.MinValue and <= int.MaxValue;
        double bestOverlap = 0;
        int best = 0;
        if (validPosition)
        {
            for (int i = 0; i < usable.Length; i++)
            {
                var d = usable[i];
                double right = saved.X + saved.Width * d.Scaling;
                double bottom = saved.Y + saved.Height * d.Scaling;
                double w = Math.Max(0, Math.Min(right, (double)d.X + d.Width) - Math.Max(saved.X, d.X));
                double h = Math.Max(0, Math.Min(bottom, (double)d.Y + d.Height) - Math.Max(saved.Y, d.Y));
                double overlap = w * h;
                if (overlap <= bestOverlap) continue;
                bestOverlap = overlap;
                best = i;
            }
        }
        var area = usable[best]; // callers pass the primary display first
        double width = Math.Min(Math.Max(640, saved.Width), area.Width / area.Scaling);
        double height = Math.Min(Math.Max(420, saved.Height), area.Height / area.Scaling);
        int physicalWidth = Math.Min(area.Width, (int)Math.Round(width * area.Scaling));
        int physicalHeight = Math.Min(area.Height, (int)Math.Round(height * area.Scaling));
        int x = bestOverlap > 0
            ? (int)Math.Clamp(Math.Round(saved.X), area.X, (double)area.X + area.Width - physicalWidth)
            : area.X + (area.Width - physicalWidth) / 2;
        int y = bestOverlap > 0
            ? (int)Math.Clamp(Math.Round(saved.Y), area.Y, (double)area.Y + area.Height - physicalHeight)
            : area.Y + (area.Height - physicalHeight) / 2;
        return new WindowPlacement { X = x, Y = y, Width = width, Height = height, Maximized = saved.Maximized };
    }
}
