# Arbitration: analyzer shadow-copy improvements

Status: normative adjudication for the next proposal revision. No implementation
or source proposal has been changed by this pass.

## Authority and decision rules

The decision baseline, in descending order, is:

1. The original request summarized as `U` in `review-response/README.md`: assess
   and specify improvements, test the loader before a large redesign, and allow
   an explicitly documented limitation. It did not mandate hot reload, ALC,
   MVCC, a shared persistent cache, or automatic retention.
2. The shipped constraints recorded in `docs/analyzer-shadow-copy/`: fix the
   wrong-path and real-output-lock problems; never apply an overlay-bearing
   `Solution` to the real `MSBuildWorkspace`; keep semantic reads overlay-aware;
   remove overlay changes before workspace persistence; do not add generated-name
   search to this work.
3. The explicit invariants in the improvement-series README, interpreted as
   target/release invariants rather than claims about v1.3.5.
4. Verified code facts at commit `3dcd63f7f814e6ac366c409693b34a8492130307`.

Neither the proposal, review, nor response is otherwise presumed correct.
Unverified runtime behavior is not treated as fact.

## Outputs

- [finding-verdicts.md](finding-verdicts.md) — independent, traceable verdict for
  every original finding.
- [arbitration-summary.md](arbitration-summary.md) — compact decision matrix.
- [normative-change-set.md](normative-change-set.md) — changes required in the
  next proposal revision.
- [unresolved.md](unresolved.md) — decisions blocked on evidence or an explicit
  product choice.
- [cross-cutting-risks.md](cross-cutting-risks.md) — consistency constraints
  across accepted findings.

No `NEW-ARB-xxx` finding is added. The material additional risks found during
adjudication were already identified in Astra's response summary as N-01–N-07;
they are incorporated where relevant rather than relabeled as newly discovered.
