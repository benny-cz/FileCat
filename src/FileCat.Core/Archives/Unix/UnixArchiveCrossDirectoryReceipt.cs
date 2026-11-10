namespace FileCat.Core.Archives.Unix;

/// <summary>Observed names after an unregistered two-parent swap; never grants cleanup authority.</summary>
internal sealed record UnixArchiveCrossDirectoryReceipt(
    string PublicParentIdentity,
    string PublicLeaf,
    string PrivateParentIdentity,
    string PrivateLeaf,
    ArchiveObjectVersion Published,
    ArchiveObjectVersion Retired);
