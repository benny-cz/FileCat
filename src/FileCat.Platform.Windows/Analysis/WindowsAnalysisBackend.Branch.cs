using System.Runtime.InteropServices;
using FileCat.Core.Analysis;

namespace FileCat.Platform.Windows.Analysis;

public sealed unsafe partial class WindowsAnalysisBackend
{
    internal WindowsAnalysisBackend OpenBranch(IReadOnlyList<AnalysisBranchComponent> components, CancellationToken cancellation)
    {
        Guard();
        AnalysisBranchComponent.Validate(components, windows: true);
        if (RootEntry.Kind != AnalysisKind.Directory || RootEntry.Cloud == true)
            throw new NotSupportedException("This source cannot verify a branch refresh.");
        ValidateReadScope(RootEntry);

        var held = new List<OwnedHandle>();
        try
        {
            SafeHandle parent = _root.Value;
            AnalysisEntry? last = null;
            foreach (var component in components)
            {
                cancellation.ThrowIfCancellationRequested();
                var next = OpenChild(parent, component.Name, directory: true);
                held.Add(next);
                parent = next.Value;
                if (Volume(parent) != _volume) throw new AnalysisSourceChangedException();
                last = Observe(parent, [], Raw(component.Name), _namespace, _nativeTimePrecisionTicks);
                component.RequireMatch(last);
            }

            ValidateReadScope(RootEntry);
            cancellation.ThrowIfCancellationRequested();
            string canonical = Root.CanonicalLocator.TrimEnd('\\') + "\\" + string.Join('\\', components.Select(component => component.Name));
            var branch = new WindowsAnalysisBackend(this, held[^1], last!, canonical);
            held.RemoveAt(held.Count - 1);
            try { AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse()); }
            catch
            {
                // The branch cannot escape if closing an intermediate handle
                // fails. Dispose its independently owned final handle too.
                branch.Dispose();
                throw;
            }
            finally { held.Clear(); }
            return branch;
        }
        finally { AnalysisNativeHandle.CloseAll(held.AsEnumerable().Reverse()); }
    }

    private WindowsAnalysisBackend(WindowsAnalysisBackend parent, OwnedHandle root, AnalysisEntry entry, string canonical)
    {
        _root = root;
        _nativeClose = parent._nativeClose;
        _volume = parent._volume;
        _namespace = parent._namespace;
        _nativeTimePrecisionTicks = parent._nativeTimePrecisionTicks;
        Root = new("1", "file", canonical, canonical, $"{_volume:X}:{entry.Identity}", IdentityAssurance.Verified, entry.Name)
        {
            Kind = AnalysisKind.Directory,
            NameRules = AnalysisNameRules.Windows,
            ConnectionIdentityReference = _bindingId.ToString("N")
        };
        RootEntry = Bind(entry, null);
        root.ReassignCloseFailure(RecordCloseFailure);
    }
}
