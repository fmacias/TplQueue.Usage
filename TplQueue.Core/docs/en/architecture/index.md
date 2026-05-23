# Architecture

This section groups the repository-level architectural material for `TplQueue.Core`.

## Main topics

- runtime overview, goals, and use cases
- job graph model and root-terminal rule
- queue semantics for parallel, FIFO, and cache-backed flows
- retry-policy selection points
- observer publication behavior
- cache orchestration boundaries

## Diagrams

Architecture diagrams are grouped here:

- [Diagrams](diagrams.md)

The source `.puml` files are stored in `docs/architecture/diagrams/`, and the rendered `.svg` output is stored in `docs/architecture/rendered/`.

## Deeper detail

The previous long-form repository guide is preserved in [../reference.md](../reference.md).
