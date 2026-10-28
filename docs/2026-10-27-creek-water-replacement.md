# Water replacement and follow-up tuning — 2026-10-27

Implemented head-driven damped flow, no new airborne emission, compatible legacy settling and saves; shared ripple map and two-phase flow advection, screen refraction and optional substrate approximation (VIVARIUM_WATER_REFRACTION=substrate); reduced texture state, continuous pool reconstruction, bounded terrain cosmetic elevation within 0.5mm of physical bed.

User reviewed initial appearance positively and requested faster spreading, visible slope films and real-time ripples. Follow-up: damping .92 -> .96 per physical step; rendered film minimum elevation 0.8mm above support only where water is stored; lower alpha cutoff; independent frame-driven optical clock respecting pause and simulation speed. Physical volume unchanged by visual film.

Validation: game build passes (three existing warnings); 57 focused hydrology/contact/tool/persistence tests pass after tuning. Initial basin native screen and cheap modes passed conservation/save reload. Initial 8-second motion capture at build/creek-replacement-20261027/basin-final/motion-8hz.mp4; 7.983 physical seconds /8.000 wall, backlog .0146 seconds, 53.75 FPS with capture overhead, not a general performance guarantee. These captures precede follow-up tuning. Latest tuning native evidence: build/creek-replacement-20261027/tuning-01. Populated creek review did not produce completed evidence; do not claim full-map acceptance. Earlier source hash manifest precedes tuning.

Existing unrelated dirty work and open editors preserved. Restart running game for C# changes. No commits. Graph was stale and not treated as current validation.
