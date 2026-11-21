using FileCat.Core.FileSystem;

namespace FileCat.Core.Permissions;

/// <summary>A tri-state mode draft whose visible octal input and submitted change share one validation state.</summary>
internal sealed class UnixPermissionModeDraft
{
    private const UnixFileMode All = UnixPermissions.Access | UnixPermissions.Special;
    private UnixFileMode _known, _mode, _touched;
    private bool _typed;

    internal UnixPermissionModeDraft(Func<UnixFileMode, bool?> initial)
    {
        for (int index = 0; index < 12; index++)
        {
            var bit = (UnixFileMode)(1 << index);
            ApplyBit(bit, initial(bit));
        }
        SyncOctal();
    }

    internal string Octal { get; private set; } = string.Empty;
    internal bool IsValid { get; private set; } = true;
    internal UnixFileMode? Mode => _known == All ? _mode : null;
    internal bool? State(UnixFileMode bit) => (_known & bit) != 0 ? (_mode & bit) != 0 : null;

    internal void ChangeBit(UnixFileMode bit, bool? value)
    {
        ApplyBit(bit, value);
        _touched = value.HasValue ? _touched | bit : _touched & ~bit;
        SyncOctal();
    }

    internal void EditOctal(string? text)
    {
        _typed = true;
        Octal = text ?? string.Empty;
        IsValid = UnixPermissions.TryParseOctal(Octal, out var mode);
        if (!IsValid) return;
        _mode = mode;
        _known = _touched = All;
    }

    internal bool TryGetChange(string? visibleOctal, out ChangePermissionMode change)
    {
        change = new((ushort)_mode, (ushort)_touched);
        if ((visibleOctal ?? string.Empty) != Octal || !IsValid) return false;
        return !_typed || (UnixPermissions.TryParseOctal(visibleOctal, out UnixFileMode visibleMode) && visibleMode == _mode);
    }

    private void SyncOctal()
    {
        _typed = false;
        IsValid = true;
        Octal = Mode is { } mode ? UnixPermissions.Octal(mode) : string.Empty;
    }

    private void ApplyBit(UnixFileMode bit, bool? value)
    {
        _known = value.HasValue ? _known | bit : _known & ~bit;
        _mode = value == true ? _mode | bit : _mode & ~bit;
    }
}
