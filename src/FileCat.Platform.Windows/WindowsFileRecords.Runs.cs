using System.Globalization;
using FileCat.Core.Inspect;
using FileCat.Core.Records;

namespace FileCat.Platform.Windows;

/// <summary>
/// The runs of every non-resident attribute in a file's MFT record (D-56, as administrator on NTFS): where each stretch
/// of its streams, index, bitmaps, attribute list, EAs, and EFS data is on the volume, as the record says, checked
/// against the VCNs its header names and the volume's size.
/// </summary>
public sealed partial class WindowsFileRecords
{
    private sealed partial class Reader
    {
        /// <summary>The most attributes whose runs are listed; each lists at most <see cref="ExtentsShown"/> runs.</summary>
        private const int RunGroupsShown = 64;

        private InspectionSection? RunsSection(NtfsRecord record)
        {
            // An attribute split over extension records is one attribute in pieces, each with the runs of its VCNs.
            var groups = record.Attributes.Where(a => !a.Resident).GroupBy(a => (a.Type, a.Name)).ToList();
            if (groups.Count == 0) return null;
            var children = new List<InspectionSection>();
            bool damaged = false;
            foreach (var group in groups.Take(RunGroupsShown))
            {
                var pieces = group.OrderBy(a => a.StartVcn).ToList();
                var first = pieces[0];
                var runs = pieces.SelectMany(a => a.Runs).OrderBy(r => r.Vcn).ToList();
                // Lengths come from disk: summed wide, so damage cannot wrap them round.
                Int128 stored = 0, sparse = 0;
                foreach (var run in runs)
                    if (run.IsSparse) sparse += run.Length;
                    else stored += run.Length;
                var lines = new List<string>();
                foreach (var piece in pieces)
                {
                    Int128 covered = 0;
                    foreach (var run in piece.Runs) covered += run.Length;
                    Int128 expected = (Int128)piece.LastVcn - piece.StartVcn + 1;
                    if (covered == expected) continue;
                    damaged = true;
                    lines.AddRange(Wrap($"Its runs from VCN {piece.StartVcn:N0} cover {Count(covered)} clusters, but its header says they reach VCN {piece.LastVcn:N0} ({Count(expected)} clusters).", "⚠ "));
                }
                int outside = runs.Count(r => !OnVolume(r));
                if (outside > 0)
                {
                    damaged = true;
                    lines.AddRange(Wrap($"{outside:N0} of its runs point past the end of the volume{(_volumeClusters > 0 ? $" ({_volumeClusters:N0} clusters)" : "")}.", "⚠ "));
                }
                if (first.CompressionUnit is > 0 and < 31)
                    lines.AddRange(Wrap($"Compressed in units of {1L << first.CompressionUnit:N0} clusters: a unit that compresses keeps fewer clusters, and a sparse run fills it up."));
                string name = first.TypeName + (first.Name.Length > 0 ? ":" + first.Name : "");
                string title = $"{name} ({runs.Count:N0} run{(runs.Count == 1 ? "" : "s")}: {Count(stored)} cluster{(stored == 1 ? "" : "s")} stored"
                    + (sparse > 0 ? $", {Count(sparse)} sparse" : "") + (pieces.Count > 1 ? $"; in {pieces.Count:N0} pieces" : "") + ")";
                var rows = runs.Take(ExtentsShown).Select(r => new[]
                {
                    r.Vcn.ToString("N0", CultureInfo.CurrentCulture), r.IsSparse ? "—" : r.Lcn.ToString("N0", CultureInfo.CurrentCulture),
                    r.Length.ToString("N0", CultureInfo.CurrentCulture), Where(r, first.IsCompressed),
                }).ToList();
                children.Add(new InspectionSection(title, [])
                {
                    Table = new InspectionTable(["VCN", "LCN", "Clusters", "Where"], rows) { More = runs.Count > ExtentsShown ? $"{runs.Count - ExtentsShown:N0} more runs are not listed." : null },
                    Lines = lines,
                });
            }
            if (damaged) _warnings.Add("MFT record: some runs do not fit its attributes or the volume (see Runs).");
            var notes = Wrap("Each run is a stretch of an attribute's clusters, from its virtual cluster (VCN), kept at a cluster of the volume (LCN); a sparse run has nothing stored.");
            if (groups.Count > RunGroupsShown) notes.AddRange(Wrap($"{groups.Count - RunGroupsShown:N0} more non-resident attributes are not listed."));
            return new InspectionSection("Runs of its non-resident attributes", []) { Lines = notes, Children = children };
        }

        private string Where(NtfsRun run, bool compressed)
        {
            if (run.IsSparse) return compressed ? "not stored (compressed away, or sparse)" : "sparse (nothing stored)";
            if (!OnVolume(run)) return "PAST THE END OF THE VOLUME";
            return _clusterSize > 0 ? $"at byte {run.Lcn * _clusterSize:N0}" : "";
        }

        private static string Count(Int128 value) => value.ToString("N0", CultureInfo.CurrentCulture);
    }
}
