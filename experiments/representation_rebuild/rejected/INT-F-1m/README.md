# Rejected INT-F1m candidate

Decision and metrics: ../../INT-F.md. This archive preserves the exact five-file tracked diff against f35bee5a, plus the two new review-runner files which are not included in a normal git diff. Source restoration removed only this experiment from the live game. The camera-manifest harness improvement was retained without flora-cache counter references.

Reconstruct only in an isolated source copy of f35bee5a: git apply candidate.patch, then copy FloraCacheReviewRunner.cs.txt and FloraMutationProbe.cs.txt to game/App with their .cs names. Build solution then game serially. Applying this full patch to a later checkout that already has the camera-manifest change will conflict; do not apply it to the dirty live main checkout.

The candidate is not production accepted. Its camera/guardian/locality checks passed, but the single later live pair had 7.14% more GPU time and 7.98% more draws with essentially equal pacing, geometry and memory. The comparison includes the Coverage snapshot fix as well as flora changes; it is not an isolated attribution experiment. Exact native assemblies and raw logs/samples remain under build/int_f, identified by the copied hashes. Recompile hashes may differ.

Re-run the retained comparison analyzer with: python analyze_comparison.py E:/Vivarium/build/int_f
