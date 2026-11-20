# Recipe candidate staging

File recipe previews start with every row excluded. The user can inspect selected rows, include or exclude candidates, and add the included entries to the review basket. Close is the preserving default. Marks and existing basket membership stay independent. The memory and indexed baskets both allow inspection and removal of captured references.

Staging checks the exact finished live revision, its runtime roots, current file and parent identity, recorded native timestamps, and the runtime FileCat protection boundary. This route admits verified single-link regular files. Changed, protected, and unverified rows stay out of the basket. Reopened inventories can still show recorded candidates. Linux staging requires a mount that exposes the full filesystem root. The resulting references carry no authority to modify a filesystem.

The first r1389 run passed 49 cases per platform. The two new cases put their ordinary source under the application directory and the protection guard refused it. The r1392 rerun moved the test source to a dedicated directory outside the application and profile. It passed both cases per platform. Only the test assembly and its symbols differed. The corrected cases cover explicit inclusion and exclusion, Close, a protected redirected backup directory, a hard-link alias, a changed file, basket removal, and unchanged marks and source bytes.

The r1390 native FileCat workflow exercised Include, Exclude, recorded-path inspection, staging one archive, and removing its reference on Windows, macOS, and Ubuntu. The original marks and basket were preserved. The Mac compositor capture confirmed that inclusion state, the action controls, path evidence, and the preserving default were visible. All task-owned processes exited. [Exact three-platform evidence](source-manifest-r1393-recipe-staging.json)

Generic cleanup review and execution, protected-object checks for those operation boundaries, and physical input and assistive technology qualification remain open.
