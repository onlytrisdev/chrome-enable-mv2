# Local semantic MV2 discovery experiment

Date: 2026-10-29. Status: **read-only research**, not enabled in the launcher.

## Question and result

Can local machine-code analysis reduce per-Chrome-version signature updates?
The same prototype independently recovered all six manifest-gate branches used
by the existing verified rules on two local x64 binaries, without reading those
rules or taking their offsets as input. It also reported one additional gate
inside the startup-manager function. This supports further work, not universal
future-version compatibility or automatic patch authorization.

| Local binary | Resource ID found in its English PAK | Existing manifest gates recovered | Additional lead | Scan time |
| --- | --- | --- | --- | --- |
| 156.0.8078.12 | 8121 | 6/6 | 1 in startup manager | 4.683 s |
| 157.0.8103.15 | 8130 | 6/6 | 1 in startup manager | 3.726 s |

Times are one local run each, excluding PE loading/initialization; they are not
user-machine performance guarantees. The six gates comprise two object-checker
copies, one integer checker, disable, re-enable and installation-policy gates.
The existing startup-loop JNE patch is **not** independently selected by this
prototype. An inlined per-extension gate inside that function is only a lead
for investigating an alternative, not an approved replacement.

Reports, local hashes and the baseline comparison are under
`E:\Project\ManifestV2\work\semantic-auto-probe\validation.json`.
The scanner does not invoke the old locator; a separate validation command
invoked `mv2ctl analyze` only after discovery to compare results.

## What the prototype does

1. Parse GRIT PAK v4/v5 resources and aliases. Search for the exact English
   unsupported-manifest error, obtaining its ID from the installed resources.
2. Find decoded MOV-immediate references to that ID inside executable code,
   using PE exception metadata to corroborate containing function ranges.
3. Use ISA-level CMP filters to generate possible manifest-version comparisons.
   No Chrome-version-specific byte patterns or stored field offsets are used.
4. Decode bounded control-flow graphs. Identify type comparisons `{1,3,8}` or
   their optimized bitset representation, and component-location exclusions
   `{5,10}`. The semantic constants are still authored assumptions.
5. Correlate installation-policy leads with the PAK-anchored function; identify
   compact checker leads and functions referencing disable-reason `0x800000`.
6. Emit diagnostic leads only. It never writes target bytes or operates on a
   Chrome process.

In a separate **synthetic in-memory mutation**, the Chrome 157 error-ID immediate
was changed from 8130 to 65000 and paired with a synthetic PAK. That integer also
appeared in an unrelated MOV elsewhere in the DLL. The CFG-feature intersection
still singled out the original installation gate. This is a controlled static
test, not a real Chrome build or functional browser result.

## REA evidence and provider boundary

REA opened the installed Chrome 157 DLL and inspected its load configuration,
including CFG declarations. The REA MCP objdump lane could not launch the default
`objdump` command on this Windows host. The installed REA CLI then successfully
ran section-header inspection using an explicit existing GNU objdump executable
via process-local `REA_OBJDUMP_COMMAND`; no global configuration was changed.
The reported artifact digest was unchanged before/after that inspection.

Capstone, **not REA decompilation**, performed this prototype's instruction and
CFG decoding. No claim of a completed Ghidra analysis is made. The retained REA
evidence is exported under `work\semantic-auto-probe`.

## Reproduce

Developer dependencies used: Python, `capstone 5.0.7`, `pefile 2024.8.26`.
They are research dependencies, not new launcher/runtime requirements.

```powershell
python E:\Project\ManifestV2\scripts\research\semantic_mv2_probe.py `
  --dll 'C:\Program Files\Google\Chrome\Application\157.0.8103.15\chrome.dll' `
  --pak 'C:\Program Files\Google\Chrome\Application\157.0.8103.15\Locales\en-US.pak'

python -m unittest discover -s E:\Project\ManifestV2\scripts\research -p 'test_*.py' -v
```

Sixteen synthetic parser/decoder tests passed. They cover PAK aliases, bounds,
mixed binary resources, optimized type-bitsets, unreachable decoy constants,
missing unwind entries for leaf candidates and bounded CFG failure. They are
not semantic-equivalence proofs or runtime extension tests.

## Requirements before production use

- Replace feature co-occurrence with register/data-flow normalization and
  abstract interpretation of the affected/unaffected paths. Same constants in
  a function are not sufficient evidence of the intended decision.
- Validate all copies and policy callbacks, not merely the install rejection.
  Resolve the startup loop or prove a per-extension alternative independently.
- Require complete accepted control flow. Some reported clone CFGs deliberately
  remain incomplete because of indirect/tail targets or the node budget.
- Preserve MV3, component-extension and unrelated-disable-reason behavior.
- Perform a sandbox-profile MV2 install and persistent-background check, then
  a full browser restart, before caching an automatically derived patch plan.
  This is a smoke test, not a substitute for static constraints.
- Cache by exact DLL digest and necessary resource identities; invalidate when
  they change. Validate bytes again before any RAM edit; apply edits atomically.
- Retain the existing verified locator as the default until the above is met.
  Ambiguity or unsupported instructions must reject discovery, not select the
  highest-scoring candidate and patch it.

The production engine could use the .NET Iced decoder plus project-owned
CFG/data-flow logic; distributing REA, Ghidra, Python, or an AI service to end
users is not necessary for that design. REA remains an authoring/inspection aid,
not a guarantee that every future binary can be understood automatically.

Even a successful semantic engine cannot restore MV2 runtime code that a future
Chrome build has actually removed. New compiler transformations or changed
semantics may still require an engine update.

## Primary references

- [Chromium GRIT PAK reader](https://raw.githubusercontent.com/chromium/chromium/main/tools/grit/grit/format/data_pack.py)
- [Microsoft x64 exception handling and RUNTIME_FUNCTION](https://learn.microsoft.com/en-us/cpp/build/exception-handling-x64)
- [Iced decoder and instruction-information API](https://github.com/icedland/iced)
