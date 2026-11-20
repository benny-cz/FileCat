# I594 — time-only edit appeared as an attribute job

## User consequence and distinct scope

An isolated exact-`05167b40` Mac app opened **File → Inspect → Change attributes and times…** for an owned `photo-note.txt` and changed only Modified from `2025-01-02 03:04:00` to `2024-01-02 03:04:00` local time. The completed job was titled **Change attributes of "photo-note.txt"**. That history entry describes a permissions/flag change rather than the time edit the user actually made, which matters when reviewing or reporting metadata changes. This is separate from I589's Properties fallback and the accepted permission-bit edit; neither checked the job title for a time-only request.

Independent Mac `stat` showed the expected modified epoch `1704161040`, unchanged created epoch `1735783440`, mode `100644` and 19 bytes. SHA-256 remained `47f7c7854f3787cc98028b4f45e7dec9e6ba4d0668b7d46a51625605fad70fa0`; the sibling target was empty. The before-state test process was stopped.

## Correction and bounded verification

Product `ccf7b64c` builds the Attributes-job title from the captured change set: modified time, creation time, both file times, attributes, or attributes and times. It does not change what the executor writes. A focused Core title test passed **1/1** on Windows x64 across all five title cases.

A clean Mac checkout at the exact source built local package ZIP SHA-256 `23ebae5951890a95fdbfbe0d5cec8ea4db3ff4b1779849ff7690702b41137849`; staged App DLL SHA-256 `ad4fb76936de46ed1e069f42d81a0285f5a326d1a7ef73a8bff8c8d43b7098f2`. The extracted bundle passed strict deep code-signature verification. With the same owned file, a fresh isolated app changed only Modified to `2023-01-02 03:04:00`; the completed native job now said **Change modified time of "photo-note.txt"**. Independent `stat` showed modified epoch `1672625040`, created epoch still `1735783440`, mode `100644`, 19 bytes and the same SHA-256; target remained empty. The current row's AX help reflected the 2023 time. An earlier focus-return announcement retained the pre-edit time; it was a historical focus message, not the current row's metadata, and this bounded check does not claim how every screen reader treats it. The isolated process was stopped.

## Disposition and finite candidate rule

Medium audit-clarity issue, **remediated preliminarily**. On the exact installed 1.0.0 candidate, edit only an owned file's modified time through the native dialog and require a completed job explicitly titled **Change modified time**, a matching current row/stat time and unchanged bytes, creation time and mode. Use the focused title control for the other four cases rather than replaying them all natively. Stop after this affected route and latest-product five-lane CI with skip reconciliation. Public preview.5 predates I594; stable 1.0.0 remains **NO-GO** until candidate gates and explicit owner GO. The active exploratory clean streak resets to **0/20**.
