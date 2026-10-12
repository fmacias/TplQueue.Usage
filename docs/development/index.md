# Development

Extend the existing Blazor sample from its current source and public package
contracts. Start with the [architecture guide](../architecture/blazor-consumer-sample.md)
and [component diagram](../architecture/blazor-sample-components.puml), then select
a scenario from the [use-case catalog](simulation-use-case-plan.md#use-case-catalog).
Each `simulation-tasks/ucXX-*.md` specifies one capability, responsible components,
dependencies and expected observable behavior.

Implementation stays inside TplQueue.Usage. Product requirements belong in the
[README Roadmap](../../README.md#roadmap), with the affected use-case ID.
Installed marketplace skills supply generic engineering guidance. The human
developer owns operational execution; the use-case documents are specifications.

## Supporting references

- [Repository boundaries and component ownership](../../Agents.md)
- [Local package-consumption reference for the human developer](local-development.md)
- [Completed preparation milestones](simulation-use-case-plan.md#completed-milestones)
- [Historical continuous-demo and UC01 record](simulation-continuous-demo.md)

Historical records explain earlier decisions. They are not active implementation
instructions, current launch settings or fresh runtime validation.
