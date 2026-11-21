# Explicit metadata sort-key selection

F-META requires unavailable keys to sort last in both directions. An explicitly supplied `MetadataField.SortKey` can return `null` when a displayed value has no qualified ordering value. The previous `CaptureKey` path then fell back to the raw comparable payload, and the App capture repeated that fallback. For the built-in Version field, an unparseable displayed version became a text key beside parsed `System.Version` keys, so the cross-type comparison fell back to names rather than unknown-last placement.

`MetadataField.CaptureKey` now uses the raw comparable value only when a field has no `SortKey` selector. The App sort capture honors that result for retained analysis values. A Core test verifies both parsed/unparseable Version values and the retained fallback for a field without a selector. A headless App test analyzes three numeric-text rows, including an unparseable value, and verifies numeric order with the unknown row last in ascending and descending sorts.

The focused Core test passed 1/1 and the headless App test passed 1/1 on Windows x64 with .NET 10.0.101. These controls verify key selection and row order for this case; they do not close the broader F-META cache, provider or native coverage gates.
