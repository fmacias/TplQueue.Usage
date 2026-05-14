# Overview

`TplQueue.Usage` is the public verification surface for the TplQueue package line.

The repository exists so a consumer can inspect:

- how to compose jobs and job roots through the public packages
- how to create queues through the adapter facade
- how observer subscriptions behave from the outside
- how to validate package-based behavior without private source access

This repository is intentionally package-oriented. Tests and samples are written the way an external application would consume TplQueue:

- package references
- consumer-owned configuration
- consumer-owned observers or built-in observers created through the public factory

The repository carries the public-safe integration scenarios adapted from `TplQueue.Core/test/Fmacias.TplQueue.Integration.Test`, after removing private project-reference assumptions. The `samples` folder is the facility-style example surface for consumers, with [QueueObserverConsole](../samples/QueueObserverConsole/README.md) documenting its supported execution modes and expected queue behavior.
