// Writes each frame of FileCat's Windows icon as a PNG of its own (filecat-16.png … filecat-256.png), so that the Linux
// and macOS packages take their small icons from the frames drawn for small sizes — the 16- and 24-pixel ones by hand —
// rather than from the large artwork shrunk, which reads poorly at those sizes.
// usage: dotnet run --project eng/IconFrames -- <filecat.ico> <out-dir>
if (args.Length != 2)
{
    Console.Error.WriteLine("usage: dotnet run --project eng/IconFrames -- <filecat.ico> <out-dir>");
    return 2;
}
byte[] icon = File.ReadAllBytes(args[0]);
if (icon.Length < 6 || BitConverter.ToUInt16(icon, 0) != 0 || BitConverter.ToUInt16(icon, 2) != 1)
{
    Console.Error.WriteLine($"{args[0]} is not an icon file.");
    return 1;
}
Directory.CreateDirectory(args[1]);
int count = BitConverter.ToUInt16(icon, 4);
for (int i = 0; i < count; i++)
{
    int entry = 6 + 16 * i;
    int width = icon[entry] == 0 ? 256 : icon[entry];
    int size = BitConverter.ToInt32(icon, entry + 8), offset = BitConverter.ToInt32(icon, entry + 12);
    var frame = icon.AsSpan(offset, size);
    // Every frame of FileCat's icon is a PNG; an old-style bitmap frame would need converting, which this does not do.
    if (!frame.StartsWith(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }))
    {
        Console.Error.WriteLine($"The {width}-pixel frame is not a PNG.");
        return 1;
    }
    string name = Path.Combine(args[1], $"filecat-{width}.png");
    File.WriteAllBytes(name, frame.ToArray());
    Console.WriteLine(name);
}
return 0;
