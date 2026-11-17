using System.Security.Cryptography;
using FileCat.Core.Threading;

namespace FileCat.Core.Analysis;

public static partial class AnalysisComparisonFormat
{
    public static Task<AnalysisSavedIndexedComparison> ReadPrivateIndexedAsync(DeviceIoScheduler io,
        AnalysisIoDevices storageDevices, string inputDevice, string name, Func<AnalysisPrivateDirectory> createScratch,
        Func<AnalysisProfileQuota> createProfileQuota, Func<AnalysisPrivateDirectory> openDirectory, CancellationToken cancellation = default)
    {
        AnalysisPrivateInventoryStore.ValidateComparisonName(name); ArgumentNullException.ThrowIfNull(openDirectory);
        return ReadExternalIndexedAsync(io, storageDevices, inputDevice, createScratch, createProfileQuota,
            () => AnalysisPrivateInventoryStore.BeginComparisonRead(openDirectory(), name, cancellation), cancellation);
    }
    /// <summary>Validate both entire inputs into bounded private graphs without materializing snapshot arrays.</summary>
    public static async Task<AnalysisSavedIndexedComparison> ReadExternalIndexedAsync(DeviceIoScheduler io,
        AnalysisIoDevices storageDevices, string inputDevice, Func<AnalysisPrivateDirectory> createScratch,
        Func<AnalysisProfileQuota> createProfileQuota, Func<AnalysisInventoryInput> openInput, CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(storageDevices);
        ArgumentNullException.ThrowIfNull(createScratch); ArgumentNullException.ThrowIfNull(createProfileQuota); ArgumentNullException.ThrowIfNull(openInput);
        if (AnalysisAdmission.Refusal() is { } refusal) throw new InvalidOperationException(refusal);
        var devices = storageDevices.Include(inputDevice);
        await ExternalLoads.WaitAsync(cancellation).ConfigureAwait(false);
        long reservation = 0;
        AnalysisScratchWorkspace? workspace = null; IndexedInput? input = null;
        AnalysisIndexedInventory? previous = null, current = null;
        AnalysisSavedIndexedComparison? result = null;
        try
        {
            AnalysisInventoryFormat.Reserve(2L * AnalysisLimits.RecordBytes, ref reservation);
            await AnalysisIo.Run(io, devices, token =>
            {
                token.ThrowIfCancellationRequested();
                AnalysisPrivateDirectory? scratch = createScratch(); AnalysisProfileQuota? quota = null;
                try
                {
                    quota = createProfileQuota() ?? throw new InvalidOperationException("Private profile capacity is required for a comparison import.");
                    workspace = new(scratch, AnalysisLimits.ScratchBytes, AnalysisScratchBudget.Shared, quota); scratch = null;
                    input = new(openInput());
                    input.Verify();
                    if (input.Length is < 1 or > AnalysisLimits.InventoryBytes) throw Invalid("The comparison exceeds the 8 GiB input limit.");
                }
                finally
                {
                    try { if (quota is not null) AnalysisIo.CloseOnWorker(quota); }
                    finally { if (scratch is not null) AnalysisIo.CloseOnWorker(scratch); }
                }
                return true;
            }, cancellation).ConfigureAwait(false);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            byte[] header = await AnalysisIo.Run(io, devices, token =>
            { input!.Verify(); var line = Line(input.Buffered, HeaderBytes, token); input.Verify(); return line; }, cancellation).ConfigureAwait(false);
            var parsed = await Task.Run(() => { cancellation.ThrowIfCancellationRequested(); return ParseHeader(header); }, cancellation).ConfigureAwait(false);
            digest.AppendData(header);
            using (var before = new Slice(input!.Buffered, parsed.PreviousBytes, digest, cancellation))
            {
                previous = await AnalysisInventoryFormat.LoadExternalIndexedAsync(workspace!, io, storageDevices, inputDevice,
                    () => new EmbeddedInput(input, before, "previous"), cancellation).ConfigureAwait(false);
                if (before.Remaining != 0 || previous.SourceBytes != parsed.PreviousBytes) throw Invalid("Previous comparison input has trailing data.");
            }
            using (var now = new Slice(input!.Buffered, parsed.CurrentBytes, digest, cancellation))
            {
                current = await AnalysisInventoryFormat.LoadExternalIndexedAsync(workspace!, io, storageDevices, inputDevice,
                    () => new EmbeddedInput(input, now, "current"), cancellation).ConfigureAwait(false);
                if (now.Remaining != 0 || current.SourceBytes != parsed.CurrentBytes) throw Invalid("Current comparison input has trailing data.");
            }
            if (previous.InventoryId != parsed.PreviousId || current.InventoryId != parsed.CurrentId)
                throw Invalid("Saved comparison input identity mismatch.");
            byte[] trailer = await AnalysisIo.Run(io, devices, token =>
            {
                input!.Verify(); var line = Line(input.Buffered, 256, token);
                if (input.Buffered.ReadByte() != -1) throw Invalid("Saved comparison has trailing data.");
                input.Verify(); return line;
            }, cancellation).ConfigureAwait(false);
            await Task.Run(() =>
            {
                cancellation.ThrowIfCancellationRequested(); using var end = Document(trailer); Fields(end.RootElement, "kind", "sha256");
                if (Text(end.RootElement, "kind") != "comparison-trailer" || Text(end.RootElement, "sha256") != Convert.ToHexStringLower(digest.GetCurrentHash()))
                    throw Invalid("Saved comparison checksum is invalid.");
            }, cancellation).ConfigureAwait(false);
            long bytes = checked(header.Length + parsed.PreviousBytes + parsed.CurrentBytes + trailer.Length);
            if (bytes != input!.Length) throw Invalid("Saved comparison size changed during import.");
            var joined = AnalysisPrivacy.Join(previous.Descriptor.Taint, current.Descriptor.Taint);
            if ((joined & parsed.Taint) != parsed.Taint) throw Invalid("Saved comparison privacy declarations disagree with their inputs.");
            ValidateRoots(previous.Descriptor.Roots, current.Descriptor.Roots, parsed.Roots);
            if (parsed.View.FocusNode != 0)
            {
                await using var focused = await (parsed.View.PreviousFocus ? previous : current).AcquireAsync(cancellation).ConfigureAwait(false);
                try { await focused.ReadPathAsync(parsed.View.FocusNode, cancellation).ConfigureAwait(false); }
                catch (ArgumentOutOfRangeException error) { throw new InvalidDataException("Saved comparison focus does not belong to its captured input.", error); }
            }
            cancellation.ThrowIfCancellationRequested();
            result = new(parsed.Id, previous, current, parsed.View, parsed.Roots, parsed.Label, parsed.SavedUtc, parsed.Taint,
                BindRootDisplayIds(previous.Descriptor.Roots, parsed.PreviousRootIds), BindRootDisplayIds(current.Descriptor.Roots, parsed.CurrentRootIds));
            previous = current = null;
        }
        finally
        {
            try
            {
                try { await AnalysisIndexedComparison.CloseAsync(current, previous).ConfigureAwait(false); }
                finally
                {
                    try { if (input is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, input).ConfigureAwait(false); }
                    finally { if (workspace is not null) await AnalysisIo.ReleaseScratchAsync(io, devices, workspace).ConfigureAwait(false); }
                }
            }
            catch { if (result is not null) await result.DisposeAsync().ConfigureAwait(false); throw; }
            finally { AnalysisInventoryFormat.Release(reservation); ExternalLoads.Release(); }
        }
        return result ?? throw Invalid("The comparison did not finish validation.");
    }

    private sealed class IndexedInput : IAnalysisScratchOwned
    {
        private readonly AnalysisInventoryInput _input;
        internal BufferedStream Buffered { get; }
        internal long Length { get; }
        internal string Identity => _input.Identity;
        internal IndexedInput(AnalysisInventoryInput input)
        {
            _input = input;
            try { input.Verify(); Length = input.Stream.Length; Buffered = new(new NonClosing(input.Stream), 64 * 1024); }
            catch { AnalysisIo.CloseOnWorker(input); throw; }
        }
        internal void Verify() => _input.Verify();
        public void Dispose() { try { Buffered.Dispose(); } finally { _input.Dispose(); } }
    }
    // This child owns no native source handle. Its outer owner stays alive until
    // both imports and outer framing validation have actually completed.
    private sealed class EmbeddedInput(IndexedInput parent, Slice slice, string side) : AnalysisInventoryInput
    {
        private bool _disposed;
        internal override Stream Stream { get { Verify(); return slice; } }
        internal override string Identity => parent.Identity + "/embedded-" + side;
        internal override void Verify() { ObjectDisposedException.ThrowIf(_disposed, this); parent.Verify(); }
        public override void Dispose() => _disposed = true;
    }
}
