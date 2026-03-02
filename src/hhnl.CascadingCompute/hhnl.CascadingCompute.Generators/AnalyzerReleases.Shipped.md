## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CCG001 | CascadingCompute | Error | Class with `[CascadingCompute]` methods must be `partial`.
CCG002 | CascadingCompute | Warning | Method signature unsupported for wrapper generation (for example `void`, `static`, non-ordinary, or `ref`/`out`).
CCG003 | CascadingCompute | Error | Interface with `[CascadingCompute]` methods must be `partial`.
CCG004 | CascadingCompute | Warning | Multiple cascading interfaces found; only one interface wrapper is supported.
