namespace FileCat.Core.Archives.Unix;

/// <summary>Observed names after an unregistered two-parent swap; never grants cleanup authority.</summary>
internal sealed record UnixArchiveCrossDirectoryReceipt(
    string PublicParentIdentity,
    string PublicLeaf,
    string PrivateParentIdentity,
    string PrivateLeaf,
    ArchiveObjectVersion Published,
    ArchiveObjectVersion Retired);

/// <summary>
/// The native exchange call failed and both names were proven to still bind their original objects. Nothing was
/// published; both held bindings remain usable. Deliberately not an IOException, so no caller mistakes it for an
/// uncertain namespace effect.
/// </summary>
internal sealed class UnixArchiveExchangeNotPerformedException(int errno)
    : Exception("The file system refused the archive name exchange (errno " + errno + "); both names are unchanged.")
{
    internal int Errno { get; } = errno;
}
