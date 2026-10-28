namespace FileCat.Archives.Rar;

internal sealed partial class RarArchive
{
    private void ReadRar4Volume(Stream stream, int volume, CancellationToken ct) => throw RarData.Unsupported("RAR 1.5–4.x archives are not read yet");
}
