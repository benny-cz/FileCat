using System.Buffers.Binary;
using System.Security.Cryptography;
using FileCat.Core.Jobs;
using FileCat.Core.Permissions;
using FileCat.Platform.Windows.Analysis;
using FileCat.Platform.Windows.Permissions;

namespace FileCat.Platform.Windows.Elevation;

internal enum FileSecurityOutcome : byte { Unavailable = 2, Declined = 3, Inspected = 4, ChangeReported = 5 }
internal enum FileSecurityChangeStatus : byte { Verified = 1, Conflict, FailedBeforeChange, Uncertain }
internal readonly record struct FileSecurityDescriptorRead(PermissionReadStatus Status, byte[]? Descriptor = null, int NativeError = 0);
internal sealed record FileSecurityInspection(FileSecurityDescriptorRead Discretionary, FileSecurityDescriptorRead Audit);
internal sealed record FileSecurityApply(FileSecurityChangeStatus Status, uint NativeError = 0, string? ObservedRevision = null);
internal sealed record FileSecurityReply(FileSecurityOutcome Outcome, FileSecurityInspection? Inspection = null, FileSecurityApply? Apply = null);

/// <summary>The control exchange inside a pipe whose kernel peers have already been verified.</summary>
internal sealed class FileSecurityPipeProtocol
{
    private const byte Request = 1;
    private const int ObservationSize = 9;
    private const int ApplySize = 37;
    private readonly byte[] _request;
    private readonly PermissionSections _sections;
    private readonly ElevatedFileSecurityChange? _change;

    internal FileSecurityPipeProtocol(string nonce, string planHash, PermissionSections inspectionSections = PermissionSections.None,
        ElevatedFileSecurityChange? change = null)
    {
        if (!NtfsMetadataProtocol.ValidNonce(nonce) || !NtfsMetadataProtocol.ValidNonce(planHash) || inspectionSections != 0 && change is not null)
            throw new ArgumentException("Invalid file security request binding.");
        byte[] identifier = Convert.FromHexString(nonce), digest = Convert.FromHexString(planHash);
        _request = [(byte)ElevationPlan.FileSecurityVersion, Request, .. identifier, .. digest];
        _sections = inspectionSections;
        _change = change;
    }

    /// <summary>Send one claim and accept only the reply for that exact claim.</summary>
    internal async Task<FileSecurityReply> RequestAsync(Stream pipe, CancellationToken cancellation)
    {
        try
        {
            await pipe.WriteAsync(_request, cancellation).ConfigureAwait(false);
            byte[] header = new byte[_request.Length];
            await pipe.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
            if (header[0] != _request[0] || !CryptographicOperations.FixedTimeEquals(header.AsSpan(2), _request.AsSpan(2)))
                throw InvalidReply();
            var outcome = (FileSecurityOutcome)header[1];
            if (outcome is FileSecurityOutcome.Unavailable or FileSecurityOutcome.Declined) return new(outcome);
            if (outcome == FileSecurityOutcome.ChangeReported && _change is not null)
                return new(outcome, Apply: await ReadApplyAsync(pipe, cancellation).ConfigureAwait(false));
            if (outcome != FileSecurityOutcome.Inspected || _sections == 0) throw InvalidReply();

            var discretionary = await ReadObservationAsync(pipe, _sections & WindowsPermissionService.DiscretionarySections, cancellation)
                .ConfigureAwait(false);
            var audit = await ReadObservationAsync(pipe, _sections & PermissionSections.Audit, cancellation).ConfigureAwait(false);
            return new(outcome, new(discretionary, audit));
        }
        catch
        {
            pipe.Dispose();
            throw;
        }
    }

    /// <summary>Runs the approved request callback on the helper's consent thread.</summary>
    internal bool Serve(Stream pipe, Func<Func<bool>, CancellationToken, FileSecurityReply?> dispatch, CancellationToken cancellation)
    {
        byte[] request = new byte[_request.Length];
        pipe.ReadExactlyAsync(request, cancellation).AsTask().GetAwaiter().GetResult();
        if (!CryptographicOperations.FixedTimeEquals(request, _request))
            throw new InvalidDataException("The file security frame does not match this request.");
        using var stopReading = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var departure = StopOnDepartureAsync();
        bool Connected() => !operation.IsCancellationRequested;
        try
        {
            if (!Connected() || dispatch(Connected, operation.Token) is not { } reply || !Connected()) return false;
            WriteReplyAsync(pipe, reply, operation.Token).GetAwaiter().GetResult();
            return true;
        }
        finally
        {
            stopReading.Cancel();
            departure.GetAwaiter().GetResult();
        }

        async Task StopOnDepartureAsync()
        {
            await ObserveDepartureAsync(pipe, stopReading.Token).ConfigureAwait(false);
            operation.Cancel();
        }
    }

    // After the request the client sends no more bytes. Either EOF or unexpected input ends the authorization.
    internal static async Task ObserveDepartureAsync(Stream pipe, CancellationToken cancellation)
    {
        try { _ = await pipe.ReadAsync(new byte[1], cancellation).ConfigureAwait(false); }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    private async Task WriteReplyAsync(Stream pipe, FileSecurityReply reply, CancellationToken cancellation)
    {
        var discretionary = _sections & WindowsPermissionService.DiscretionarySections;
        var audit = _sections & PermissionSections.Audit;
        if (reply.Outcome == FileSecurityOutcome.Inspected)
        {
            if (_sections == 0 || reply.Inspection is not { } observed || reply.Apply is not null) throw InvalidReply();
            ValidateObservation(observed.Discretionary, discretionary);
            ValidateObservation(observed.Audit, audit);
        }
        else if (reply.Outcome == FileSecurityOutcome.ChangeReported)
        {
            if (reply.Apply is not { } applied || reply.Inspection is not null) throw InvalidReply();
            ValidateApply(applied);
        }
        else if (reply.Outcome is not (FileSecurityOutcome.Unavailable or FileSecurityOutcome.Declined) ||
                 reply.Inspection is not null || reply.Apply is not null)
            throw InvalidReply();

        byte[] header = (byte[])_request.Clone();
        header[1] = (byte)reply.Outcome;
        await pipe.WriteAsync(header, cancellation).ConfigureAwait(false);
        if (reply.Inspection is { } inspection)
        {
            await WriteObservationAsync(pipe, inspection.Discretionary, cancellation).ConfigureAwait(false);
            await WriteObservationAsync(pipe, inspection.Audit, cancellation).ConfigureAwait(false);
        }
        if (reply.Apply is { } result)
        {
            byte[] payload = new byte[ApplySize];
            payload[0] = (byte)result.Status;
            BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(1), result.NativeError);
            if (result.ObservedRevision is { } revision) Convert.FromHexString(revision).CopyTo(payload, 5);
            await pipe.WriteAsync(payload, cancellation).ConfigureAwait(false);
        }
    }

    private async Task<FileSecurityApply> ReadApplyAsync(Stream pipe, CancellationToken cancellation)
    {
        byte[] payload = new byte[ApplySize];
        await pipe.ReadExactlyAsync(payload, cancellation).ConfigureAwait(false);
        string? revision = payload.AsSpan(5).IndexOfAnyExcept((byte)0) < 0 ? null : Convert.ToHexString(payload.AsSpan(5));
        var result = new FileSecurityApply((FileSecurityChangeStatus)payload[0],
            BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(1)), revision);
        ValidateApply(result);
        return result;
    }

    private void ValidateApply(FileSecurityApply result)
    {
        if (_change is null || result.ObservedRevision is { } observed &&
            (!NtfsMetadataProtocol.ValidNonce(observed) || observed.AsSpan().IndexOfAnyExcept('0') < 0))
            throw InvalidReply();
        bool valid = result.Status switch
        {
            FileSecurityChangeStatus.Verified => result.NativeError == 0 && result.ObservedRevision == _change.Expected,
            FileSecurityChangeStatus.Conflict => result.NativeError == 0 && result.ObservedRevision is null,
            FileSecurityChangeStatus.FailedBeforeChange => result.ObservedRevision is null ||
                result.NativeError != 0 && result.ObservedRevision == _change.Before,
            FileSecurityChangeStatus.Uncertain => true,
            _ => false,
        };
        if (!valid) throw InvalidReply();
    }

    private static async Task WriteObservationAsync(Stream pipe, FileSecurityDescriptorRead observation, CancellationToken cancellation)
    {
        var header = new byte[ObservationSize];
        header[0] = (byte)observation.Status;
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(1), observation.NativeError);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(5), observation.Descriptor?.Length ?? 0);
        await pipe.WriteAsync(header, cancellation).ConfigureAwait(false);
        if (observation.Descriptor is { } descriptor)
            await pipe.WriteAsync(descriptor, cancellation).ConfigureAwait(false);
    }

    private static async Task<FileSecurityDescriptorRead> ReadObservationAsync(Stream pipe, PermissionSections sections, CancellationToken cancellation)
    {
        var header = new byte[ObservationSize];
        await pipe.ReadExactlyAsync(header, cancellation).ConfigureAwait(false);
        var status = (PermissionReadStatus)header[0];
        int length = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(5));
        if (length < 0 || length > PermissionSnapshots.MaxSectionBytes ||
            (length > 0) != (status == PermissionReadStatus.Present) || sections == 0 && length != 0)
            throw InvalidReply();
        byte[]? descriptor = null;
        if (length != 0)
        {
            descriptor = new byte[length];
            await pipe.ReadExactlyAsync(descriptor, cancellation).ConfigureAwait(false);
        }
        var result = new FileSecurityDescriptorRead(status, descriptor, BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(1)));
        ValidateObservation(result, sections);
        return result;
    }

    private static void ValidateObservation(FileSecurityDescriptorRead report, PermissionSections sections)
    {
        if (sections == 0)
        {
            if (report.Status != PermissionReadStatus.Unloaded || report.Descriptor is not null || report.NativeError != 0)
                throw InvalidReply();
        }
        else if (report.Status == PermissionReadStatus.Present)
        {
            if (report.Descriptor is not { Length: >= 20 and <= PermissionSnapshots.MaxSectionBytes } descriptor || report.NativeError != 0)
                throw InvalidReply();
            _ = WindowsPermissionDescriptor.Control(descriptor);
            if (UnexpectedOffset(descriptor, 4, sections, PermissionSections.Owner) ||
                UnexpectedOffset(descriptor, 8, sections, PermissionSections.Group) ||
                UnexpectedOffset(descriptor, 12, sections, PermissionSections.Audit) ||
                UnexpectedOffset(descriptor, 16, sections, PermissionSections.Access | PermissionSections.Inheritance))
                throw InvalidReply();
        }
        else if (report.Descriptor is not null || report.Status is not (PermissionReadStatus.Denied or PermissionReadStatus.Unsupported or
                     PermissionReadStatus.Failed or PermissionReadStatus.Stale or PermissionReadStatus.Gone or PermissionReadStatus.Incomplete))
            throw InvalidReply();
    }

    private static bool UnexpectedOffset(byte[] descriptor, int offset, PermissionSections actual, PermissionSections allowed) =>
        (actual & allowed) == 0 && BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(offset, 4)) != 0;

    private static InvalidDataException InvalidReply() => new("The file security reply does not match this request.");
}
