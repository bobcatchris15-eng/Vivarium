# Current human handoff — living landscape rendering

User request 2026-10-08: "Alright, look at the docs. Clank em up."
Authoritative plan: docs/photorealism/living_landscape_rendering_implementation_plan.md; asset evidence contract: docs/photorealism/reference_dossier_template.md.

Execute first increment P00–P03 only. G0 requires real target-machine fixed-scene baseline, honest failures and measured bottlenecks. Later asset/shader/render ownership tasks remain dependency-gated; real reference dossiers block mesh/material production. Human aesthetic review is still required for later visual acceptance.

Current source baseline: c71ebe8. Implementation owner: isolated .clanker/wt/landscape-baseline. Independent validator: .clanker/wt/landscape-review. No pushes or release publication. Main's existing 17 fluid-related/ledger modified or untracked files are protected by .clanker/history/2026-10-08-landscape-upgrade/main-local-file-hashes.json. Never stage/checkpoint all of main.

All GPU tests must run alone. No 30-FPS, full-suite, memory-soak or visual-success claim exists. G0 is unpassed. Reference plan requests complete frame times, per-second FPS, process/VRAM trends, CPU/GPU timing distinction, source/hardware/settings provenance, deterministic scene queries and digest invariants.

Historical Pilot Tree handoffs/checkpoints remain in .clanker/history/2026-10-08-landscape-upgrade/. They do not replace this goal. Read .clanker/current-work.md and .clanker/tasks/landscape-baseline/task-state.json on resume, then command-backed receipt from isolated worker. Independent findings must be resolved or explicitly carried forward.
