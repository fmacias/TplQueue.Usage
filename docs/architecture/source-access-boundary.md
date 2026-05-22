# Source-access boundary

`TplQueue.Usage` is public-facing, but `TplQueue.Core` source access remains intentionally restricted.

That split is deliberate:

- public consumers should be able to evaluate the binaries, examples, queue behavior, and observer flow
- internal implementation details in `TplQueue.Core` remain outside the public repository boundary
- this repository therefore validates behavior through packages rather than through direct project references into private source

In practice, that means:

- samples and tests in this repository consume `Fmacias.TplQueue` packages
- local preview validation uses the workspace-local package feed `..\TplQueue.NugetLocal`
- published preview and stable validation can later resolve the same package IDs from `nuget.org`

This repository is the public usage and verification surface.

Private source access, if granted at all, is a separate operational and legal decision outside the scope of `TplQueue.Usage`.
