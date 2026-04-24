# Agent Instructions

## Specs Are Required For New Work

For every new feature, behavior change, architectural change, tool change, or non-trivial bug fix, agents must create, maintain, and validate a spec under `specs/`.

- Before changing code, search `specs/` for an existing relevant spec.
- If a relevant spec exists, update it in the same work item so it reflects the intended and implemented behavior.
- If no relevant spec exists, create a new spec directory using the next numeric prefix and a concise kebab-case name, for example `specs/003-wasm-version-checker/spec.md`.
- Specs for existing features may be written as reverse specs, but must still document the current behavior, requirements, edge cases, non-goals, and validation strategy.
- Do not consider implementation complete until code, tests, and specs agree.
- When fixing regressions, add or update the spec with the scenario that should remain protected.
- Keep specs professional and product-focused. Do not include conversational notes, internal chat context, or personal references.

## Spec Validation Checklist

Before finalizing work, agents must verify:

- The relevant spec exists and is linked to the behavior being changed.
- Functional requirements match the code that was actually implemented.
- User scenarios or acceptance scenarios cover the important success paths.
- Edge cases mention known compatibility, security, migration, or failure-mode constraints.
- Tests or manual validation steps are listed or implied by the success criteria.
- Any out-of-scope behavior is explicitly called out when it could otherwise be assumed.
