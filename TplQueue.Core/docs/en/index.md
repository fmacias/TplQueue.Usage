# TplQueue.Core docs

This folder contains the repository-level documentation for `TplQueue.Core`.

Use the tree below as the stable entry point for future MkDocs import.

## Tree

- [Usage](usage/index.md)
- [Architecture](architecture/index.md)
- [Development](development/index.md)
- [Operations](operations/index.md)
- [Full reference](reference.md)
- [License model](license.md)

## Scope

`TplQueue.Core` owns the execution kernel of the TplQueue line.

It is the right repository when you need to understand:

- job graph execution semantics
- queue dispatch behavior
- payload-aware runtime nodes
- queue event publication
- runtime-level release and signing constraints

This `TplQueue.Usage/TplQueue.Core/docs/en/` tree is the public source of truth mirrored into the `Core Engine` section on `fmacias.github.io`.

It is maintained in `TplQueue.Usage` because the `TplQueue.Core` source repository remains private.
