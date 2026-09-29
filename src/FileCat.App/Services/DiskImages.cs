namespace FileCat.App.Services;

/// <summary>The disk images recovery reads, by their usual names: raw images and fixed VHDs (plan §17).</summary>
internal static class DiskImages
{
    public static readonly string[] Extensions = [".img", ".dd", ".bin", ".raw", ".ima", ".vhd", ".001"];

    public static bool IsImageName(string name) => Extensions.Contains(Path.GetExtension(name), StringComparer.OrdinalIgnoreCase);
}
