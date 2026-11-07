namespace FileCat.Core.Analysis;

public sealed partial class AnalysisPrivateInventoryStore
{
    internal AnalysisPrivateDirectory BorrowDirectory()
    { lock (_gate) { Guard(); return new BorrowedDirectory(this, directory); } }
    internal AnalysisPrivateFile OpenCatalogLock(CancellationToken cancellation)
    {
        lock (_gate)
        {
            Guard(); cancellation.ThrowIfCancellationRequested();
            const string marker = "FileCat catalog write lock v1\n";
            byte[] expected = System.Text.Encoding.ASCII.GetBytes(marker); AnalysisPrivateFile file;
            try { file = directory.OpenRead(AnalysisPrivateNames.CatalogWriteLock); }
            catch (FileNotFoundException)
            {
                try { WriteMetadataNew(AnalysisPrivateNames.CatalogWriteLock, expected, cancellation); }
                catch (AnalysisPublicationUncertainException error) { throw new IOException("The private catalog lock durability is uncertain. No inventory was saved.", error); }
                catch (IOException error) when (!AnalysisCloseUnconfirmedException.IsPresent(error)) { }
                file = directory.OpenRead(AnalysisPrivateNames.CatalogWriteLock);
            }
            try
            {
                file.Verify(); if (file.Stream.Length != expected.Length) throw new InvalidDataException("Unsupported private catalog lock. Existing bytes were preserved.");
                byte[] actual = new byte[expected.Length]; file.Stream.ReadExactly(actual); file.Verify();
                if (!actual.AsSpan().SequenceEqual(expected)) throw new InvalidDataException("Unsupported private catalog lock. Existing bytes were preserved.");
                if (!file.TryLockCapacity()) throw new IOException("Another saved-analysis writer is active. Keep existing files and try again when it finishes.");
                return file;
            }
            catch { AnalysisIo.CloseOnWorker(file); throw; }
        }
    }
    private sealed class BorrowedDirectory(AnalysisPrivateInventoryStore owner, AnalysisPrivateDirectory source) : AnalysisPrivateDirectory
    {
        private bool _closed;
        internal override void Verify() { ObjectDisposedException.ThrowIf(_closed, this); owner.Guard(); source.Verify(); }
        internal override (UInt128 Capacity, UInt128 Available) Space() { Verify(); return source.Space(); }
        internal override AnalysisPrivateFile CreateStaging() { Verify(); return source.CreateStaging(); }
        internal override AnalysisPrivateScratchFile CreateScratch() { Verify(); return source.CreateScratch(); }
        internal override AnalysisPrivateFile OpenRead(string name) { Verify(); return source.OpenRead(name); }
        internal override string[] Entries(CancellationToken cancellation) { Verify(); return source.Entries(cancellation); }
        public override bool MatchesRoot(IAnalysisBackend root) { Verify(); return source.MatchesRoot(root); }
        public override void Dispose() => _closed = true;
    }
}
