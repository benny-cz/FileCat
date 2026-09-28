using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using FileCat.App.Services;
using FileCat.Core.Commands;

namespace FileCat.App.Views;

/// <summary>Small, theme-aware vector symbols for the commands in FileCat's context menus.</summary>
internal static class MenuIconFactory
{
    internal static Image Create(string id)
    {
        var palette = ThemeManager.Current;
        string color = id is CommandIds.Delete or CommandIds.DeletePermanent or "tab.close" or "tab.closeOthers" ? palette.Error
            : id is CommandIds.Open or CommandIds.Copy or CommandIds.OpenInNewTab or CommandIds.OpenInTarget ? palette.ActiveAccent
            : palette.TextMuted;
        string path = id switch
        {
            CommandIds.Open => "M2,4 L6,4 L7.4,5.5 L14,5.5 L14,12.5 L2,12.5 Z M5,8 L10.5,8 M8.5,6.5 L10.5,8 L8.5,9.5",
            CommandIds.OpenWithSystem => "M2.5,5 L2.5,13.5 L11,13.5 L11,10 M7,2.5 L13.5,2.5 L13.5,9 M7.5,8.5 L13.5,2.5",
            CommandIds.View => "M1.5,8 C4,3.8 12,3.8 14.5,8 C12,12.2 4,12.2 1.5,8 Z M8,6.2 A1.8,1.8 0 1 0 8,9.8 A1.8,1.8 0 1 0 8,6.2",
            CommandIds.Edit or CommandIds.Rename => "M2.5,13.5 L5.8,12.7 L13.5,5 L11,2.5 L3.3,10.2 Z M9.8,3.7 L12.3,6.2",
            CommandIds.Copy or CommandIds.Duplicate => "M5,3 L13.5,3 L13.5,12 L5,12 Z M2.5,5.5 L2.5,14 L11,14",
            CommandIds.Move => "M2,4 L7,4 L7,12 L2,12 Z M8,8 L14,8 M11.5,5.5 L14,8 L11.5,10.5",
            CommandIds.Delete or CommandIds.DeletePermanent => "M3,4.5 L13,4.5 M6,3 L10,3 M4.5,5 L5,13 L11,13 L11.5,5 M7,7 L7,11 M9,7 L9,11",
            CommandIds.CopyToClipboard => "M4,4 L12,4 L12,14 L4,14 Z M6,2.5 L10,2.5 L10,5 L6,5 Z M6,8 L10,8 M6,10.5 L10,10.5",
            CommandIds.CutToClipboard => "M3.5,4 A1.5,1.5 0 1 0 3.5,7 A1.5,1.5 0 1 0 3.5,4 M3.5,10 A1.5,1.5 0 1 0 3.5,13 A1.5,1.5 0 1 0 3.5,10 M5,6 L13,12 M5,11 L13,5",
            CommandIds.PasteFromClipboard => "M4,4 L12,4 L12,14 L4,14 Z M6,2.5 L10,2.5 L10,5 L6,5 Z M8,7 L8,11 M6.5,9.5 L8,11 L9.5,9.5",
            CommandIds.CopyPaths => "M2,11 L5,11 L7,8 L10,8 L12,5 L14,5 M2,3 L6,3 M2,6 L5,6 M10,12 L14,12",
            CommandIds.CopyNames => "M2.5,4 L4,4 M6,4 L13.5,4 M2.5,8 L4,8 M6,8 L13.5,8 M2.5,12 L4,12 M6,12 L13.5,12",
            CommandIds.OpenInNewTab or "tab.duplicate" => "M1.5,4 L6,4 L7.3,5.5 L14.5,5.5 L14.5,13 L1.5,13 Z M8,7.5 L8,11 M6.3,9.25 L9.7,9.25",
            CommandIds.OpenInTarget or "tab.moveTarget" or "tab.copyTarget" => "M1.5,3 L6.5,3 L6.5,13 L1.5,13 Z M9.5,3 L14.5,3 L14.5,13 L9.5,13 Z M5,8 L11,8 M8.5,5.5 L11,8 L8.5,10.5",
            CommandIds.Reveal => "M1.5,4 L6,4 L7.2,5.5 L14.5,5.5 L14.5,13 L1.5,13 Z M8,8 L11,8 M9.5,6.5 L9.5,9.5",
            CommandIds.OpenTerminal => "M2,3 L14,3 L14,13 L2,13 Z M4,6 L6.5,8 L4,10 M8,10.5 L12,10.5",
            CommandIds.Checksum => "M5.5,2.5 L4.5,13.5 M10.5,2.5 L9.5,13.5 M2.5,6 L13.5,6 M2,10 L13,10",
            CommandIds.Properties => "M3,3 L13,3 L13,13 L3,13 Z M5,6 L11,6 M5,8 L11,8 M5,10 L9,10",
            "tab.close" or "tab.closeOthers" => "M3,3 L13,13 M13,3 L3,13",
            "tab.lock" => "M4,7 L12,7 L12,14 L4,14 Z M5.5,7 L5.5,5 A2.5,2.5 0 0 1 10.5,5 L10.5,7 M8,10 L8,12",
            "tab.left" => "M13,8 L3,8 M6,5 L3,8 L6,11",
            "tab.right" => "M3,8 L13,8 M10,5 L13,8 L10,11",
            _ => "M3,3 L13,3 L13,13 L3,13 Z M5,6 L11,6 M5,9 L10,9",
        };
        var brush = new SolidColorBrush(Color.Parse(color));
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing { Geometry = new RectangleGeometry(new Rect(0, 0, 16, 16)), Brush = Brushes.Transparent });
        drawing.Children.Add(new GeometryDrawing
        {
            Geometry = Geometry.Parse(path),
            Pen = new Pen(brush, 1.25, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round),
        });
        return new Image { Source = new DrawingImage(drawing), Width = 16, Height = 16 };
    }
}
