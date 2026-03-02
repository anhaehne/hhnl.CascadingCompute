## Release 0.1.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
CCG001 | CascadingCompute | Error |
CCG002 | CascadingCompute | Warning |
CCG003 | CascadingCompute | Error |
CCG004 | CascadingCompute | Warning |

### Notes

- `CCG001`: Reported when a class containing `[CascadingCompute]` methods is not declared `partial`, which blocks wrapper generation.
- `CCG002`: Reported for unsupported method signatures (for example `void`, `static`, non-ordinary methods, or methods with `ref`/`out` parameters); these members are skipped for wrapper generation.
- `CCG003`: Reported when an interface containing `[CascadingCompute]` methods is not declared `partial`, which blocks interface wrapper generation.
- `CCG004`: Reported when a class implements multiple interfaces with cascading-compute methods; only one interface wrapper is generated.
