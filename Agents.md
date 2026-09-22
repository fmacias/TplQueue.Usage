# AGENTS.md — Contract-Driven Client–Server Samples
## Context

You are working in the `TplQueue.Usage` repository.

This repository is the public package-consumption, sample, and validation surface for the TplQueue ecosystem.

It contains:

- runnable consumer samples
- package-based integration validation
- public-safe repository documentation under `docs/`

It does not own the private `TplQueue.Core` implementation documentation anymore. Private Core documentation now belongs in the private `TplQueue.Core` repository, while public TplQueue consumption documentation belongs in `TplQueue.Adapter/docs/<lang>/`.

Check the AGENTS_RULES.md which contains the refactor, review and implementation rules.

## Implementation Rules

Apply the parent `../AGENTS.md` and this repository's `AGENTS_RULES.md`. The maintained sample architecture is linked below.

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
7. Apply the shared review, implementation, refactor and test rules referenced by `../TplQueue.Adapter/AGENTS.md`.

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

## Sample architecture and host profiles

Read [Blazor frontend architecture and contract ownership](docs/architecture/blazor-consumer-sample.md)
before modifying `samples/TplQueue.Sample.BlazorSignalR` or its ETL integration.
That document consolidates the current architecture, generated-code policy,
alignment findings, and suggestions. Do not duplicate it in another design guide.

### Current Blazor profile

- The sample is a passive .NET 8 Interactive Server dashboard; its hosted workload runs independently of browsers.
- The current frontend is `tools/TplQueue.JobMonitor`, a reusable JavaScript Web Component with a small Razor/JS snapshot bridge. ScatterChart and vis-timeline are retired.
- The full viewer uses vertical time, real logical execution channels and explicit Unassigned placement for null channels. Root identity never determines channel placement. There is no permanent details panel.
- The overview shows five seconds ending at the bottom reference. Square centers retain exact timestamps; collisions use count markers with temporary interval inspection, never timestamp displacement. Time zoom must not widen channels. Keep the fixed left UTC ruler and keyboard access to grouped jobs.
- Assigned positions use the backend's channel-bearing Started timestamp. Preserve observed enqueue time in the typed enqueuedAt field and metadata. Retain its marker in the collapsible Unassigned strip after assignment and draw a directed enqueue-to-start connector when both endpoints are visible. These are two positions of one job, not additional jobs or dependency relationships. Keep jobs without the channel/start pair in Unassigned; never substitute Running or terminal timestamps for channel acquisition. Search reveals the selected job's strip, including retained enqueue history.
- Keep runtime behavior in C#. Preserve immutable snapshots, observer isolation, InvokeAsync dispatch, coalesced updates and async subscription/interop disposal.
- Refresh data after new accepted observer events, with one initial snapshot. Do not add polling or periodic live redraws. Advance live time on snapshot arrival; preserve paused/history references. Parent rerenders with the same snapshot must not resend it through JS interop. User input and resize may redraw locally.
- `IJobExecutionEvent` is optional additive observer metadata. Do not break existing IJobEvent implementations or fabricate channels for legacy events.
- Keep model validation, graph traversal, layout, time control and theme tokens outside the mechanical SVG renderer.
- MSBuild synchronizes shared component assets from tools into an ignored sample wwwroot directory before static asset discovery; do not edit generated copies.
- Handwritten DTOs and mappers require semantic review. Generation needs authoritative inputs, pinned tooling and repeatable validation.
- Preserve existing tests; the former ScatterChart mapper lives only in integration-test Legacy support, not in the running host.
- The standalone Node/browser harness and Debug-only Blazor circuit harness complement NUnit and HTTP smoke tests.
### Future TypeScript profile

The TypeScript/OpenAPI architecture is a separate future sample profile, not a
mandatory intermediary for Blazor. Use one chosen presentation framework per
example. Keep generated transport contracts separate from handwritten frontend
models, mappers, use cases, ports and validation. Add schema-driven editing or
shared frontend packages only when the requested feature needs them.

`QueueObserverSignalRDashboard` remains a distinct existing transport sample.
Do not attribute its endpoints or custom hub to the Blazor timeline sample.

### Verification and documentation boundaries

For code changes, use the repository scripts, NUnit and Arrange/Act/Assert;
test changed contract, projection and UI lifecycle boundaries. Do not treat HTTP
prerender checks as interactive browser coverage. For documentation-only changes,
check links, source claims and diffs; report that runtime tests were not run.

The Blazor/ETL path currently has explicit sibling-source references during the
preview migration. Preserve this documented exception and distinguish its
validation from package-only consumption. Workspace project-reference switching
remains intentional. Do not alter dependencies to satisfy a documentation task.

This public sample guide does not replace `TplQueue.Adapter/docs/<lang>/` as the
public product-documentation source or alter site synchronization. Link to the
guide; do not publish private workspace/Core documentation or mirror competing
sample architecture copies.
