# Automatic local offset discovery (3.13.0)

## Accepted grammar, not fuzzy patching

`AutomaticGateLocator` uses the Iced x64 decoder. An ISA prefilter generates
candidate CMP sites, after which decoded operands, GPR aliases, field accesses,
direct branches and supported predicate operations are checked. Instruction
lengths, branch destinations, field displacements and the unsupported-manifest
resource ID are derived locally, not selected by Chrome major version.

The engine requires six consistent object predicates and one integer predicate:
two impact checkers, installation, disable, re-enable, an inline startup gate,
and the bounded integer/bitset overload. Supported decision graphs are evaluated
over the comparison partitions, including negative/extreme values, component
locations and the extension-type override. Unknown register bits, memory reads,
unsupported operations, changed results and nonterminating graphs reject the
plan. Partial-register writes are tracked by known-bit masks.

Installation acceptance and its resource-backed rejection must converge on the
same callback. Disable and re-enable suffixes must match their reason-output
and reason-removal contracts. `StartupLoopLocator` decodes the startup predicate's
bounded `.pdata` function into a CFG. It proves that insertion and the empty/nonempty
guard use the same stack collection; checks the allocator, exact reason store,
three-field reason container and disable-call arguments; cross-correlates registrar
and extension-ID helpers with the re-enable path; and validates the backward
iterator branch and common cleanup. Exactly one forward entry must satisfy the
whole contract. Only that entry's displacement is neutralized, never a backward
loop branch. Register allocation, stack displacement, short/near JNE encoding and
branch distances are decoded, not selected by the V6/V12 startup signatures.

For compiler-adjacent cold blocks, cookie-check failure calls are treated as
nonreturning only after checking the dominating mismatch, matching stack/global
cookie and the helper's matching global comparison leading to Windows `INT 29h`
fast-fail. Merely seeing a CALL or a nearby reason constant is insufficient.
See Microsoft's [`__fastfail` contract](https://learn.microsoft.com/en-us/cpp/intrinsics/fastfail?view=msvc-170).
This remains a conservative supported grammar, **not** an arbitrary future-layout
recognizer. All edits are grouped into one
plan. Legacy fallback is limited to the older 151–154 families and known
155–158 layouts when the English PAK is absent. A failed automatic semantic
validation with the anchor present is not silently overridden by a legacy rule.

This is deliberately narrower than unrestricted binary understanding. A new
compiler transformation, renamed/missing English error string, incompatible
field structure or actual MV2 runtime removal can still require an app update.
No REA, Ghidra, Python, model or offset-download service runs on user machines.

## Cache and launch identity

Location: `%LOCALAPPDATA%\ChromeMv2Launcher\analysis-cache`.

- Cache keys use canonical DLL paths; the protected payload binds the complete
  DLL SHA-256, English PAK SHA-256 and the core engine module identity/schema.
- Current-user DPAPI protects payload integrity. Plain JSON, corrupted blobs,
  old engines, mismatched hashes, duplicate/invalid edits and wrong original
  bytes are rejected. Protection does not claim to defend against arbitrary
  code already executing as the same Windows user.
- Cache hits stream-hash the DLL and read only PE headers/planned bytes, without
  loading a second 300 MB byte array or scanning executable code. Timestamp and
  size are not a substitute for content identity.
- Writes use a unique same-directory temporary file and atomic replacement.
  Read/write failures do not block independent analysis. Only successful plans
  for original disk images are saved.
- Launch re-hashes the source DLL and holds a read-only, non-write/non-delete
  sharing handle through creation/loading. Remote original bytes are verified
  before any RAM write. Stale results cannot authorize a different DLL.

`mv2ctl analyze --no-cache --json` bypasses cache reads/writes for diagnostics.
This is independent of the launcher's existing bounded GitHub release check.

## Verification

Tests include synthetic register/field/padding changes, changed predicate
semantics, integer bit-index bounds, nontermination, missing families, PAK
bounds/aliases/ambiguity and cache integrity/atomicity/replacement cases.

Installed-binary integration compares **every edit** with the prior verified
plan; controlled in-memory resource renumbering succeeds without a new rule,
whereas mismatched PAK/resource IDs, bitsets and startup branches are rejected.
A separate copied-file test changes DLL content while preserving size/mtime,
then exercises cache invalidation, corruption recovery and resource changes.

On the local Chrome 156.0.8078.12 and 157.0.8103.15 x64 builds, the automatic
plans matched the previous edit sets. Both passed the actual Load unpacked UI
test, exact MV2 background-page detection and full browser-restart persistence
in isolated temporary profiles. This is evidence for those builds, not proof
for untested future versions.

Commands:

```powershell
dotnet test .\tests\Mv2Enabler.Tests\Mv2Enabler.Tests.csproj -c Release
.\artifacts\win-x64-self-contained\mv2ctl.exe analyze --no-cache --json
.\artifacts\win-x64-self-contained\mv2ctl.exe analyze --json
.\artifacts\win-x64-self-contained\mv2ctl.exe functional-ui-test --extension .\test-extension --json
```

Use `--filter "Category!=Integration"` on machines without the local Chrome
fixtures or an available desktop. Do not run process/UI integration tests while
the user's Chrome session is open.

### Local verification record (2026-10-29)

- 107 .NET tests passed (102 portable, 5 integration).
- 15 GUI checks passed, including one-click launch, patched-session reuse,
  failure UI, restart confirmations and verified-cache status in all three
  supported languages. The optional remote-update fixture was not used here;
  startup/update-check unit coverage remains in the .NET suite.
- Published self-contained GUI/CLI file version: 3.11.0.0.

| Chrome x64 | Forced-scan median | Cache-hit median | Samples per mode |
| --- | --- | --- | --- |
| 156.0.8078.12 | 636 ms | 269 ms | 3 |
| 157.0.8103.15 | 635 ms | 262 ms | 5 |

These are full analyzer timings, including streaming identity hashing, on this
one host. CLI process wall-time medians were 755–757 ms for forced analysis and
328–339 ms for cached analysis. They exclude GUI startup, online release checks
and Chrome launch; disk/cache/CPU conditions affect other machines.

The installed Chrome 157 DLL SHA-256 remained
`F82FFEDFAFB637A0375C82E1A5AFF9B71C12F8937E55C09C3B6BA85A8C5675FF`.
Receipts are retained locally under `work\semantic-auto-probe`, including the
final benchmark JSON, GUI integration log and both runtime/restart JSON reports.

### Local verification record (2026-11-05, 3.13.0)

- Automatic rule `chromium.mv2-semantic.local.v2`; no automatic call to the
  legacy startup signature locator. Legacy fallback rules are unchanged.
- 127 .NET tests passed: 121 portable and 6 installed/runtime integration tests.
  New fixtures cover GPR/frame changes, short/near entry branches, incorrect
  collection/extension/cleanup, reason value/address/count/span, registrar/ID
  helper mismatches, inverted entry/backedge and wrong loop exit. Invalid inline
  instruction candidates are skipped without losing subsequent valid gates.
- An in-memory copy of the installed Chrome 158 DLL was changed to reverse the
  entry CMP's register operands (equivalent for JNE). Legacy startup signatures
  rejected it, while semantic discovery approved exactly the same edit set.
  Changing the cookie helper's global or fast-fail interrupt rejected the plan.
- 15 GUI checks passed on Chrome 158, including vi/en/zh-CN cache status,
  one-click launch, windowless session reuse and restart confirmations.
- Both self-contained CLI runtime tests installed the exact MV2 smoke extension
  through the actual Load unpacked folder dialog, then found its background page
  again after a full browser restart in the same isolated profile. The dialog
  helper now sets and reads back its path directly instead of relying on global
  keyboard focus/clipboard. Test profiles were removed afterward.

| Chrome x64 | Forced-scan median | Cache-hit median | Samples per mode | MV2 UI + restart |
| --- | --- | --- | --- | --- |
| 156.0.8078.12 | 628 ms | 290 ms | 5 | PASS |
| 158.0.8127.18 | 664 ms | 310 ms | 5 | PASS |

These are local full-analyzer timings, including DLL hashing, not disk-cold I/O
or Chrome-launch timings. They do not establish support for Chrome 159 or 160.
The installed Chrome 158 DLL SHA-256 stayed
`F818FECCCDEE5C05F7A8523B990F6D820722D550C0E520EE4A093BEA2ACA7FE7`;
the Chrome 156 fixture stayed
`D41B9AA516B6DC1A785F814F49CB6D5A7B462FC33E1010E4EA7DE81345FC5BBC`.
New receipts use the `startup-v2-*` prefix under `work\semantic-auto-probe`.
Release packaging uses the same verified engine and includes self-contained
GUI/CLI ZIP archives and `SHA256SUMS.txt`.
