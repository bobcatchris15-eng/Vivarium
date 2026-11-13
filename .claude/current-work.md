# Representation rebuild — current work
Updated 2026-11-13. Canonical historical ledger: .claude/ledger.md; current execution: docs/representation-rebuild/plan.md.

Human handoff: “Okay, Go ahead, get this wrapped up and work through the remaining parts, committing and pushing regularly. Use terra agents primarily.” This resumes work after Claude quota failures, superseding the previous pause.

Goal: complete the chosen state-first representation rebuild: believable botanical and substrate geometry, attached colonies, cached lighting, causative diverse ecology, and integrated lower-cost always-on presentation. The source brief is docs/representation-rebuild/brief.md; ledger RD1–RD16 refine it.

Constraints: use main checkout, no worktrees/PRs/CI. Terra agents primarily. At most three workers; one Godot GPU process at a time; root allocates GPU lease. Keep species lifelike and distinct rather than identical to legacy. Target measured Radeon 860M only. No save/API compatibility requirement. One production quality. No per-grain whole-world simulation, no off-camera freeze, no density/exposure shortcuts. Root alone commits/pushes scoped verified milestones.

Recovered state: HEAD 72a6234, main ahead of origin/main by 28 locally recorded commits. Fresh solution build passed (9 warnings); historical experiment approvals are reported in ledger, not newly verified. INT-L stopped before edits; P2g and E1 left partial uncommitted work. Those need tests and independent review before acceptance.

Success evidence: end-to-end launch/boot with invariants=0, meaningful deterministic resource/conservation/locality checks, six rendered acceptance demos, paired real-scene performance/cost report and explicit rejected methods/limitations; current code committed and pushed.
