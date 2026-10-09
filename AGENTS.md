# graphify
- **graphify** (`~/.Codex/skills/graphify/SKILL.md`) - any input to knowledge graph. Trigger: `/graphify`
When the user types `/graphify`, use the installed graphify skill or instructions before doing anything else.

<!-- CLANKER_PROTOCOL_START -->
# CLANKER FLEET PROTOCOL — SELF-MIGRATION DIRECTIVE

This specification supersedes all previous versions of the Pointer-Based Orchestration / Clanker Fleet methodology contained in this repository.

When instructed to bootstrap or upgrade this methodology:

1. Locate the repository's applicable `AGENTS.md`.
2. Identify any existing section implementing or describing:
   - Pointer-Based Orchestration
   - Clanker Fleet Protocol
   - Clanker orchestration
   - `.clanker/`
   - external AGY/OpenCode worker routing
   - previous versions of this methodology
3. Remove the previous methodology in its entirety.
4. Preserve all unrelated `AGENTS.md` content exactly unless a change is strictly required for compatibility with this specification.
5. Insert this specification into `AGENTS.md` as the authoritative replacement.
6. Do NOT leave both old and new protocol versions in the file.
7. Do NOT summarize, abridge, reinterpret, or merge obsolete protocol rules into the replacement.
8. If explicit protocol boundary markers already exist, replace the content between them.
9. If boundary markers do not exist, add:

   `<!-- CLANKER_PROTOCOL_START -->`

   before this specification and:

   `<!-- CLANKER_PROTOCOL_END -->`

   after it.
10. Future protocol upgrades SHALL replace only the content between those markers.
11. Before modification, preserve the previous `AGENTS.md` through Git history. A separate backup file is unnecessary when the repository is clean and tracked by Git.
12. After replacement:
    - validate that unrelated repository instructions remain present;
    - validate that exactly one authoritative Clanker protocol exists;
    - initialize or migrate `.clanker/` state as required;
    - inspect installed AGY/OpenCode backends;
    - reconcile existing state with the new schema;
    - continue using the new protocol immediately.

If an existing `.clanker/` installation is present, this is an UPGRADE, not a fresh installation.

Existing durable information SHOULD be retained when compatible, including:

- task history
- receipts
- logs
- graph evidence
- learned backend success statistics
- decision records
- known constraints

Obsolete configuration fields MAY be removed or migrated.

Never discard historical evidence merely because its schema predates the current protocol.

The version contained between `CLANKER_PROTOCOL_START` and `CLANKER_PROTOCOL_END` is authoritative.
<!-- CLANKER_PROTOCOL_END -->
