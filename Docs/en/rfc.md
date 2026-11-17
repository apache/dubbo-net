# RFC conventions

[中文](../zh/rfc.md) · [English home](index.md) · [Changelog](../../CHANGELOG.md)

Write an RFC only for a real architecture decision with alternatives and trade-offs. An RFC explains **why** a decision was made; code and tests describe **what** currently runs. Track implementation progress in the linked Issue and PR, not in an RFC work log. Do not create an empty RFC for a planned feature.

## Naming and publication

- Reserve the next unused four-digit ID when the first proposal opens (for example, `0001`); never reuse an ID. For a decision intended for both language audiences, create `Docs/en/rfcs/0001-topic.md` and `Docs/zh/rfcs/0001-topic.md` in the **same PR**, with matching IDs, statuses, decisions, and links. Use a short lowercase hyphenated topic; create the `rfcs/` directories only when an actual proposal exists. Translation-only corrections retain the ID and link to their source PR.
- `Proposed`: alternatives are under review. `Accepted`: the decision was recorded. `Superseded`: a linked RFC replaced it. `Withdrawn`: the proposal was abandoned. Code existing does not by itself make a proposal accepted; link decision and implementation separately. Keep both languages in sync when status or outcome changes.

## Minimum template

Copy this outline only when drafting an actual RFC. Link a concrete Issue, decision discussion, and implementation PR/commit **when available**; label unimplemented work accordingly. A decision can be accepted before implementation without claiming that it has shipped.

```markdown
# RFC-NNNN: Short title

Status: Proposed | Accepted | Superseded | Withdrawn
Issue: link
Decision / review: link or "pending"
Implementation: PR/commit link or "not implemented"

## Context and constraints
What problem exists, and what must remain compatible?

## Options and trade-offs
Describe the chosen option and at least one realistic alternative, with costs.

## Decision
Record the choice, or "pending" while Proposed.

## Consequences
Describe limitations and follow-up work without claiming unshipped behavior.
```
