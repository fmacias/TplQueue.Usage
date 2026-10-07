# Continuous combined demo follow-up

Date: 2026-09-28. Baseline: UC01 commit `7344cd7`.

The human explicitly requested ETL and single jobs together, continuing until a
dashboard Stop button is pressed, with accepted jobs allowed to finish. This
supersedes the finite-only default for the interactive host. It does not complete
UC23's broader lifecycle work or UC25's bounded retention checklist. Existing task
execution records describe their historical acceptance and are unchanged.

Current behavior is maintained in the
[architecture guide](../architecture/blazor-consumer-sample.md#continuous-combined-demo).

## Current migration status (2026-10-06)

The record below describes the September source-based implementation and its test
results. The current staged migration uses `0.2.0-preview.2` packages and moves Domain
into Usage. It replaces configurable finite profiles with two continuous workflows,
each owning one timer across all queues; the host registers both directly. Timing
is fixed at a one-second offset and three-second interval. Old finite settings,
legacy scenario classes and several corresponding test fixtures were removed.
Those historical results do not validate the current package build. Use the
[architecture guide](../architecture/blazor-consumer-sample.md) and current review
results for the active behavior and validation limits.

## Review completion (2026-10-07)

The package migration review corrected test setup to use the registered runtime
interfaces and Domain factories, restored cancellation-cleanup and payload-hydration
coverage, and added a hydration-constructor guard against null measurements. The
integration project again builds the web hosts it launches. Component composition
files and Program.cs were not modified by the review.

All TplQueue references now use TplQueuePackageVersion, defaulting to
`0.2.0-preview.2`; command-line overrides work consistently. The local feed path
is relative to Usage's NuGet.config. Current README, architecture and development
guidance describe repository-local Domain ownership and fixed continuous delivery.

Validation against local packages, from the Usage repository root:

| Check | Result |
| --- | --- |
| `.\build.ps1 -Configuration Debug` | Passed; seven existing test warnings, no errors |
| `.\test.ps1 -Configuration Debug` | 151 passed, no failures; the subsequent coverage run also passed all 151 tests |
| `dotnet run --no-build --no-restore --configuration Debug --project samples/PackageConsumptionSmokeConsole/PackageConsumptionSmokeConsole.csproj -- all` | All six smoke modes passed |
| `.\coverage.ps1 -Configuration Debug -NoBuild -NoRestore -EnforceBaseline` | Tests passed; coverage gate failed at 92.91%, below 93.5%. Threshold unchanged |
| MSBuild property and PackageReference evaluation | All 16 default/override evaluations passed across eight projects; no alternate packages restored |
| JobMonitor syntax and layout checks | 15 modules checked and 26 tests passed on 2026-10-06 |
| Changed documentation links and whitespace | 100 local links/anchors checked; unstaged diff check passed |

The coverage filter includes the test assembly and Fmacias.TplQueue assemblies;
it does not measure the separately built TplQueue.Sample.Domain and Simulation
assemblies. Its percentage is not a complete sample-runtime coverage claim.
Interactive browser/multi-circuit acceptance was not rerun. HTTP sample tests do
not establish that acceptance. Product packaging was not rerun because product
sources were unchanged. Runtime build/test validation required execution outside
the sandbox after sandboxed builds stalled.

## Historical implementation

- Default `combined` host profile starts six module-owned scenarios: ETL and
  single jobs on FIFO, Parallel and Cache. Startup offset defaults to one second,
  interval to three seconds. Configure `Simulation:StartupOffsetSeconds` and
  `Simulation:IntervalSeconds` for this profile. One full round is six roots and
  twelve unique jobs, subject to existing busy/capacity skips.
- `SimulationScenarioSettings.Continuous` bypasses the repetition limit while
  preserving validation and admission bounds. Original finite constructors and
  the explicit `etl`/`single-job` host profiles keep their previous behavior.
- Stop arrivals invokes the shared service's existing admission barrier. It waits
  for pending submissions, not graph drain, and never cancels the host job token.
  All connected circuits observe Completion and update their stop control without
  polling or resending the unchanged monitor snapshot. Disconnecting disposes the
  circuit's wait; it does not stop the simulation. Restart requires a server restart.
- JavaScript remains the existing integrated monitor. Workload configuration,
  timers and Stop belong to C#/Blazor. No new application API or custom hub.

## Validation

Source-mode assets were restored through WorkspaceTplQueue before this follow-up.
No dependency or project reference changes were made. Supplemental logs/TRX and
the local browser runner are in workspace `out/simulation-continuous/`.

Source flags:
`-m:1 -nr:false -p:SolutionFileName=WorkspaceTplQueue.sln -p:SolutionDir=C:/Users/Fernando/source/fmacias/WorkspaceTplQueue/ -p:SkipPackLocal=true`.
Test project:
`TplQueue.Usage/test/integration/Fmacias.TplQueue.Usage.Integration.Test/Fmacias.TplQueue.Usage.Integration.Test.csproj`.

| Command from workspace root | Result |
| --- | --- |
| `dotnet test <test-project> --configuration Debug --no-restore <source-flags> --filter FullyQualifiedName~DefaultDashboard_OffersStopAndContinuesBothWorkloadsBeyondTwoTicks --logger 'trx;LogFileName=continuous-red.trx' --results-directory out/simulation-continuous` | Expected failure before implementation: no Stop control in the old default host. |
| `dotnet build <test-project> --configuration Debug --no-restore <source-flags>` | Pass, seven existing test warnings and no errors. |
| `dotnet test <test-project> --configuration Debug --no-build --no-restore --filter 'FullyQualifiedName~FiniteScenarioDeliveryTests\|FullyQualifiedName~TplQueueSampleBlazorSignalRSampleTests\|FullyQualifiedName~SingleJobSimulationTests\|FullyQualifiedName~SimulationHostedServiceTests' --logger 'trx;LogFileName=continuous-focused.trx' --results-directory out/simulation-continuous` | 32 pass. Use literal OR separators without backslashes in PowerShell. |
| `dotnet build WorkspaceTplQueue/WorkspaceTplQueue.sln --configuration Debug --no-restore -m:1 -nr:false -p:SkipPackLocal=true` | Pass after adding real-queue stop/drain coverage; seven existing warnings, no errors. |
| `dotnet test WorkspaceTplQueue/WorkspaceTplQueue.sln --configuration Debug --no-build --no-restore -m:1 -nr:false --logger 'trx;LogFilePrefix=continuous-workspace' --results-directory C:/Users/Fernando/source/fmacias/out/simulation-continuous` | 680 pass, one existing SignalR cancellation case fails in the sandbox: `Cancel_Run_CancelsCollection_AndSkipsRootSuccess`. All changed-scope tests pass, including the new combined stop/drain test. P02 records the same sandbox limitation. |
| `node --check TplQueue.Usage/tools/TplQueue.JobMonitor/tests/blazor.js` | Pass with Visual Studio's Node executable. |
| `powershell -NoProfile -ExecutionPolicy Bypass -File out/simulation-continuous/browser-live.ps1` | Not executed: automatic approval review repeatedly disconnected before completing the review. Interactive Stop/multi-circuit acceptance remains unverified. |

Node executable:
`C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Microsoft/VisualStudio/NodeJs/node.exe`.
The added browser case uses `?profile=combined`: it waits for more than two rounds,
stops during execution, checks another circuit's status, lets all accepted jobs
complete, checks stable job counts after another interval, and preserves the
existing selection/navigation/disposal checks. The query selects test expectations;
it does not configure or start the backend.

An unrestricted retry of the SignalR cancellation test also could not execute
because approval review disconnected. These were review-service failures, not
safety determinations. No browser success or all-green full-suite result is claimed.

Manual-timer tests verify continued arrivals, explicit stop, ignored late callbacks,
and stop racing an admitted submission in both finite and continuous modes. A real
timer exercises more than two arrivals. HTTP host tests retain both finite launch
modes and verify the default combined host exceeds two rounds. The additional real
queue test stops six accepted roots while their handlers run and verifies all
twelve ETL/single jobs finish successfully without cancellation, including Cache
hydration. No existing tests were deleted.

## Scope and limitations

The changed libraries remain netstandard2.0/C# 9; the host and tests remain
net8.0/C# 12. Existing project references and public documentation ownership remain
unchanged. The original unstaged plan formatting and sibling changes are preserved.
Staged review covered configuration compatibility, timer stop races, shared
completion notifications, circuit disposal and finite-profile compatibility.
No implementation blocker was found within the requested demo scope; the pending
browser check and retention limitation are explicit. Local validation resolves 28
documentation links/anchors, confirms unchanged sibling staged/unstaged diffs and
the original unstaged plan formatting, and passes `git diff --cached --check`.

The human's continuous-demo request is an explicit exception to the rollout plan,
not an implementation of bounded continuous operation: history, event fingerprints,
catalog membership, accepted-root lists and single-job measurement data still grow
until the process is restarted. Active-root admission remains bounded. Cache is
process-local, and the previously documented capacity-one Cache limitation remains.

Packaging is not applicable because no product package changes. Standalone package
build/test/coverage remain subject to the known P01-P03 dependency mismatch; this is
source validation. No product fixes, schema generation, retention subsystem,
restart action or new use-case completion claim is included.
