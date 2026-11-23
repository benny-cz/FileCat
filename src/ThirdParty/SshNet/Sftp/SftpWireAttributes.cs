#nullable enable
using System;

namespace Renci.SshNet.Sftp;

// Keep the wire presence bits and unsigned IDs independent of the mutable legacy view.
internal readonly record struct SftpWireAttributes(uint Flags, ulong? Size, uint? UserId, uint? GroupId,
    uint? Permissions, DateTime? ModifiedUtc);

internal readonly record struct SftpAccountNames(string? User, string? Group);
