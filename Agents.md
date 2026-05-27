# AGENTS.md

## Context

You are working in the `TplQueue.Usage` repository.

This repository is the public package-consumption, sample, and validation surface for the TplQueue ecosystem.

It contains:

- runnable consumer samples
- package-based integration validation
- public-safe repository documentation under `docs/`

It does not own the private `TplQueue.Core` implementation documentation anymore. Private Core documentation now belongs in the private `TplQueue.Core` repository, while public TplQueue consumption documentation belongs in `TplQueue.Adapter/docs/<lang>/`.

## Repository boundaries

When working in this repository:

1. Treat it as a public-facing repository.
2. Prefer published-package consumption flows over private-source assumptions.
3. Do not reintroduce `TplQueue.Core` private documentation as public source-of-truth content here.
4. Keep runnable examples and validation flows aligned with the published packages and the current public documentation.

## Cross-repository documentation alignment

When a task touches documentation, navigation, publishing, licensing text, or repository-boundary explanations:

1. Read the root `Agents.md` or `AGENTS.md` and the root `README.md` of every directly affected repository before editing.
2. If a more specific instruction file exists for the affected surface, read it after the root files.
3. Compare the requested change against the current documented source-of-truth model and note any contradictions before applying broad edits.
4. Do not silently normalize contradictions across repositories.
5. If the human instruction conflicts with the existing documentation or instruction files and intent is not already explicit, ask whether to:
   - align the documentation to the current human instruction and treat the inconsistency as an exception, or
   - preserve the existing inconsistency as the current operating rule.
6. When proceeding with a cross-repository documentation change, update the relevant `README.md`, `Agents.md` or `AGENTS.md`, and sync or publishing instructions together so the documentation boundary remains aligned.

## Public documentation boundary

- `TplQueue.Adapter/docs/<lang>/` is the public source of truth for the integrated TplQueue documentation.
- `TplQueue.Core/docs/<lang>/` is private Core documentation.
- `fmacias.github.io` publishes the public TplQueue documentation from `TplQueue.Adapter/docs/<lang>/`.
- `TplQueue.Usage` should link to those sources, samples, and public validation flows without duplicating private or deprecated documentation trees.

## Validation workflow

When changes are made, run the relevant public validation commands when possible:

1. `.\build.ps1`
2. `.\test.ps1`
3. `.\coverage.ps1`

If a step cannot be executed, state that clearly.
