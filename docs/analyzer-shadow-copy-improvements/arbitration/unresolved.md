# Unresolved issues

Only decisions that cannot be made from the present requirements, code, and
recorded evidence are listed here. They are not authorization to silently choose
an implementation.

## U-ARB-01 — Missing-path provenance policy

Related findings: E5-01, E5-02, E5-04, E5-05.

The proposal cannot simultaneously require proof of analyzer-to-project origin,
skip every unproved match, and guarantee recovery of the original missing-path
repro unless a usable provenance channel exists.

Missing evidence:

- The evaluated Roslyn 5.9.0/MSBuild metadata actually available for an
  `OutputItemType="Analyzer" ReferenceOutputAssembly="false"` reference.
- Whether that metadata reliably links the analyzer item to a loaded project
  across the supported configuration/TFM matrix.
- Cost and lifecycle of any extra evaluation required to obtain it.
- An execution-oracle test where a missing foreign analyzer has the same filename
  as an in-solution project's output.

Decision after evidence:

- If reliable provenance exists, use it and skip unproved missing/inaccessible
  paths.
- If it does not, the product owner must choose between retaining a clearly
  documented opt-in unique-name heuristic for missing paths or narrowing support
  and no longer fixing that class of the original repro. Inaccessible paths require
  a separate decision and must not inherit the missing-file rule automatically.

Until resolved: retain the shipped matcher and do not roll out epoch-5 matcher
changes.

## U-ARB-02 — Supported generator update mode

Related findings: E1-02, E1-03, E3-01, E3-05, E6-01.

The requirements permit a documented restart limitation and do not require
in-process hot reload. The current loader's actual V1→V2 behavior on .NET 10 has
not been measured with an exact execution oracle.

Missing evidence:

- Exact marker results for cached load, reset+load, and process restart when the
  main assembly identity is unchanged but its bytes change.
- Loaded assembly path/identity and whether a stale assembly is returned.
- Operational cost and expected frequency of a required server restart.

Decision after evidence: choose either a precisely bounded in-process update
contract or a restart-required contract. In either case, an unsupported in-process
refresh must not silently return known-stale or wrong semantics.

## U-ARB-03 — Production dependency discovery and binding scope

Related findings: E2-01, E3-02, E3-03, E3-04.

The explicit fixture can establish file-preparation and binding mechanics, but the
repository has no demonstrated production source for the complete private runtime
dependency set. Nor is requester-scoped resolution for conflicting helper versions
established.

Missing evidence:

- A stable dependency source from evaluated build metadata, a generated manifest,
  or another verified mechanism.
- Actual `AddDependencyLocation` calls and requesting-assembly behavior under the
  pinned Roslyn version.
- Type-identity results for an ALC prototype with enumerated shared host contracts.
- Conflicting-helper execution results and unload/lifetime measurements.

Decision after evidence: select scoped dependency support and, only if needed, an
ALC/resolver design. If the evidence is insufficient, explicitly support main-only
generators and fail dependency scenarios with actionable diagnostics.

## U-ARB-04 — Raw-workspace analyzer load leak

Related findings: E1-07, E4-03.

The write boundary demonstrably prevents persistence of known overlay references,
but neither side has shown which operation, if any, causes Roslyn to load an
existing analyzer directly from `workspace.CurrentSolution` before/from outside
the overlay.

Missing evidence:

- Existing-correct-path tests that execute semantics, exercise every write path,
  and record the assembly path actually loaded.
- A forced rebuild after each operation to prove or refute a real-output lock.
- Timing evidence for load before overlay enable and for raw-workspace readers.

Decision after evidence: if a leak is reproduced, specify a separate semantic/load
boundary. If it is not reproduced across the supported matrix, document the tested
boundary without claiming that the write funnel caused the anti-lock guarantee.

## U-ARB-05 — Repeated-call flag semantics

Related findings: E1-04, E6-03.

The public parameter is a non-nullable optional boolean, so the implementation
cannot distinguish an omitted value from explicit `false`. Current behavior is
session-sticky on a same-key cache hit; a reset or different load clears it. The
word “opt-in” establishes that activation is not automatic but does not establish
whether every later load call is a desired-state update.

Missing requirement/evidence:

- Whether existing clients rely on repeated omitted/false calls preserving a
  previously enabled overlay.
- Whether the product contract intends enablement per load request or per loaded
  workspace session.
- Compatibility policy if the existing sticky behavior changes.

Decision required: choose and document either desired-state semantics (`false` and
omitted disable) or session-sticky semantics (reset is the disable operation). A
tri-state/new parameter would be a public API change and requires separate
justification. Until decided, preserve and accurately document current behavior.
