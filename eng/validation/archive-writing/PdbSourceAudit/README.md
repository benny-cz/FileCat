# Compiler source audit

Development evidence tool; it grants no production qualification. Run after an owned worker build has stopped:

```sh
dotnet run --project eng/validation/archive-writing/PdbSourceAudit -c Release -- \
  /absolute/private/source /absolute/worker-publish /absolute/new-source-snapshot
```

The tool verifies compiler document checksums from every `FileCat.*.pdb` against private checkout bytes or embedded
generated source, then stores those exact bytes and binary/source identities. It refuses unknown checksums, missing
or changed sources, paths outside the checkout and oversized inventories. Keep the source snapshot with the exact
published bundle. Post-run checksum auditing does not backdate project files, verifier changes or documentation.

An optional fourth argument permits one explicit additional source root, for example an absolute NuGet package
cache when auditing test assemblies which compile xUnit runner content. The default production audit does not gain
that allowance. All additional documents still require the compiler's exact checksum and are snapshotted under the
same count/byte limits; the selected root is recorded in `facts.json`. This proves the bytes compiled, not their
upstream provenance or package integrity. Keep the dependency locks/package provenance with test evidence.
