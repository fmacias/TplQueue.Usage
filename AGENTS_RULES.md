# AGENTS.md - TplQueue.Core

## Context

This Agents.md file contains between other the rule for implementation, review , refactor and test.

## Cross-repository documentation alignment

When a task touches documentation, navigation, publishing, licensing text, or repository-boundary explanations:

1. Read the root `Agents.md` or `AGENTS.md` and the root `README.md` of every directly affected repository before editing.
2. If a more specific instruction file exists for the affected surface, read it after the root files.
3. Compare the requested change against the current documented source-of-truth model and note contradictions before broad edits.
4. Do not silently normalize contradictions across repositories.
5. If the human instruction conflicts with the existing documentation or instruction files and intent is not already explicit, ask whether to:
   - align the documentation to the current human instruction and treat the inconsistency as an exception, or
   - preserve the existing inconsistency as the current operating rule.
1. When proceeding with a cross-repository documentation change, update the relevant `README.md`, `Agents.md` or `AGENTS.md`, and sync or publishing instructions together so the documentation boundary remains aligned.
## General operating rules

1. Work only within the scope requested by the human:
   - **Review** = analyze and report
   - **Refactor** = improve existing code without changing intended behavior
   - **Implementation** = fix a bug or add a feature

2. Prefer small, safe, understandable changes unless the human explicitly asks for a broader refactor.

3. Preserve the current architectural style unless there is a clear defect, contradiction, or explicit requirement to change it.

4. Do not silently redesign the library.

5. Keep public behavior stable unless a bug fix or the explicit task requires a change.

6. Respect the existing architecture, naming, layering, and design intent before proposing broader changes.

---

## Rules for code review

When asked to perform a **code review**, follow this process:

1. Verify that the changes to be reviewed are staged in the relevant git repository.
   - If they are not staged, stop the review and inform the human.
   - Reason: staged changes are easier to inspect, discuss, and revert safely.

2. Verify the relevant project-level configuration files, such as `.props`, solution-level settings, or project settings, to confirm that:
   - the C# language version is appropriate for `.NET Standard 2.0` for the software component at following repository folders: **TplQueue.Adapter**, **TplQueue.Abstractions** and **TplQueue.Core.**
   - The project configuration is consistent with the intended compatibility targets

   If the configuration is inconsistent, stop and report the issue first.

3. Review the code according to these principles:
   - SOLID
   - DRY
   - KISS
   - YAGNI
   - Separation of Concerns
   - Fail Fast
   - Defensive programming
   - Immutability by default where reasonable
   - Readability and maintainability
   - Thread safety where relevant
   - Safe async usage
   - Serialization safety where relevant

4. Review all relevant public and internal services within the affected scope.

5. Identify and report:
   - duplication
   - long or overly complex methods
   - missing validation or guard clauses
   - hidden side effects
   - poor separation of responsibilities
   - misleading naming
   - test gaps
   - documentation gaps
   - risks to backward compatibility

6. Static helper classes:
   - should remain stateless
   - should not mutate instance state
   - should be internal unless a public static API is truly justified

7. During review, do not refactor broadly unless the human explicitly requested review plus fixes.
   - Minor non-invasive corrections are acceptable only if explicitly requested.

8. When reporting SOLID concerns, pay special attention to the user’s design preferences:
   - internal construction logic may intentionally use static factories
   - not every internal implementation is meant to be substitutable
   - testability is still required, including for internal and non-public services where appropriate

9. If you detect a major design issue involving OCP or LSP that would require architectural change rather than a safe local improvement:
   - stop before making invasive changes
   - explain the issue clearly
   - propose the safest next step

10. After review, report findings in a structured way:
   - critical issues
   - design issues
   - maintainability issues
   - test gaps
   - optional improvements

---

## Rules for refactoring

When asked to perform a **refactor**, follow all review rules above first, then:

1. Refactor only within the repository scope requested by the human.

2. Do not modify dependent repositories unless the human explicitly requested cross-repository changes.

3. If a defect in a dependency prevents a correct refactor:
   - stop
   - explain the blocking dependency
   - describe the likely fix required in the dependent component

4. Allowed refactoring actions include:
   - fixing clearly incorrect logic
   - adding guard clauses and argument validation
   - simplifying control flow
   - extracting private helper methods
   - removing dead private code
   - improving XML documentation
   - improving internal naming where it does not break the public API
   - reducing duplication
   - improving readability and cohesion

5. Refactoring must preserve intended behavior unless a bug is being fixed as part of the task.

6. Refactoring should improve code quality without introducing speculative abstractions or unnecessary architectural changes.

---

## Rules for implementation

When asked to perform an **implementation** task, including a bug fix or a new feature:

1. First apply the same analysis discipline used in review and refactor mode.

2. If the request is ambiguous, infer the safest interpretation from the codebase and surrounding context.
   - Avoid unnecessary clarification questions when the intent can reasonably be recovered from the existing code and documentation.

3. You may modify dependent repositories only if this is necessary to implement the feature or bug fix correctly and the requested scope allows cross-repository changes.

4. Prefer solutions that:
   - preserve the existing architecture
   - minimize public API changes
   - keep backward compatibility where practical
   - fit the existing code style and conventions

5. For bug fixes:
   - identify the root cause, not only the symptom
   - add or adapt tests covering the failing case

6. For new features:
   - integrate them into the existing abstractions rather than introducing parallel ad-hoc patterns
   - keep the feature extensible, but do not over-engineer

7. When implementing concurrency-related changes, pay special attention to:
   - thread safety
   - race conditions
   - cancellation flow
   - retry consistency
   - ordering guarantees
   - shared mutable state
   - async correctness
8. Apply TDD ( Test Driven Design). Upadate or add the Unit test and integration test belongs to the changes. Deletes are first not desired. prefer to have obsolete unit tests instead.

---

## Test expectations

Whenever code changes are made:

1. Update or add tests as needed.

2. Cover:
   - valid paths
   - edge cases
   - invalid arguments
   - invalid state transitions
   - exception paths

3. Use:
   - NUnit
   - Moq where appropriate
   - Arrange / Act / Assert structure

4. Keep integration tests readable and representative of real composition behavior.

5. Do not remove tests unless they are objectively invalid, obsolete, or replaced by better coverage.
   - If a test is removed, explain why.

6. Do not generate or expand tests unless they are necessary for the requested change, bug fix, or refactor scope.

# Providing a commit text ready to pase of staged changes

Sometime, the human will request from you to check the staged changes to commit, by applied differencies.
In this case, check the applied differencies, deduce the changes and provide the output humanized and summarized per implemented issue into one commit text.

---

## Build and validation workflow

When code changes are made, run the relevant validation steps in this order when possible:

1. Build the affected projects
2. Run unit tests
3. Pack locally using the repository’s local packaging script if available, for example `pack-local`
4. Run integration tests that depend on packaged outputs, if applicable

If any step cannot be executed, state that clearly and explain why.


---

## Code Documentation expectations

When creating or substantially modifying C# code:

1. Add or improve XML documentation comments in English where relevant.
2. Keep documentation technically precise, concise, and consistent with the actual behavior.
3. Do not leave misleading, outdated, or speculative comments in the code.
---
