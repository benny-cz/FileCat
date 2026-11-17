using System.Text;

namespace FileCat.Core.Analysis;

public sealed unsafe partial class UnixAnalysisBackend
{
    // The existing runtime root is already held. Only handle-relative no-follow
    // opens are used to bind the recorded branch. Its final descriptor transfers
    // directly into the new source.
    internal UnixAnalysisBackend OpenBranch(IReadOnlyList<AnalysisBranchComponent> components, CancellationToken cancellation)
    {
        Guard();
        AnalysisBranchComponent.Validate(components, windows: false);
        if (RootEntry.Kind != AnalysisKind.Directory || RootEntry.Cloud == true || !_identitySupported ||
            _mac && _modifiedPrecisionTicks != 1)
            throw new NotSupportedException("This source cannot verify a branch refresh.");
        ValidateReadScope(RootEntry);

        string relative = "";
        var parent = RootEntry;
        for (int index = 0; index < components.Count; index++)
        {
            cancellation.ThrowIfCancellationRequested();
            var component = components[index];
            relative = relative.Length == 0 ? component.Name : relative + "/" + component.Name;
            byte[] encoded = Encoding.UTF8.GetBytes(relative);
            int descriptor = OpenRelative(encoded, directory: true);
            try
            {
                var facts = Stat(descriptor);
                if (facts.Device != _device || facts.Mount != _mount) throw new AnalysisSourceChangedException();
                var observed = Bind(Entry([.. encoded], Encoding.UTF8.GetBytes(component.Name), facts), parent.Ancestry);
                component.RequireMatch(observed);
                parent = observed;
                ValidateReadScope(observed);
                if (index + 1 != components.Count) continue;

                cancellation.ThrowIfCancellationRequested();
                var owner = _handleLeases[descriptor];
                var branch = new UnixAnalysisBackend(this, descriptor, owner, facts, relative, component.Name);
                _handleLeases.Remove(descriptor);
                return branch;
            }
            finally { Close(descriptor); }
        }

        throw new InvalidOperationException("A branch needs at least one directory component.");
    }

    private UnixAnalysisBackend(UnixAnalysisBackend parent, int descriptor, AnalysisNativeHandle owner, Facts facts, string relative, string name)
    {
        _root = descriptor;
        _device = parent._device;
        _mount = parent._mount;
        _identitySupported = parent._identitySupported;
        _modifiedPrecisionTicks = parent._modifiedPrecisionTicks;
        _nativeClose = parent._nativeClose;
        string canonical = parent.Root.CanonicalLocator.TrimEnd('/') + "/" + relative;
        Root = new("1", "file", canonical, canonical, $"{facts.Device:X}:{facts.Inode:X}:{facts.Mount:X}", IdentityAssurance.Verified, name)
        {
            Kind = AnalysisKind.Directory,
            NameRules = AnalysisNameRules.Unix,
            ConnectionIdentityReference = _bindingId.ToString("N")
        };
        RootEntry = Bind(Entry([], Encoding.UTF8.GetBytes(name), facts), null);
        _handleLeases.Add(descriptor, owner);
    }
}
