# TplQueue Job Monitor

Reusable, dependency-free JavaScript Web Component for a passive queue execution
timeline. The same source runs in the standalone demo and the .NET 8 Blazor sample.
The default theme is dark; there is no permanent job details panel.

## Run and verify

With Node.js 20 or later, from this directory:

```powershell
node demo/server.mjs
node scripts/check.mjs
node --test tests/layout.test.js
```

Open `http://127.0.0.1:4178/` for the deterministic demo and
`http://127.0.0.1:4178/tests/browser.html` for browser acceptance.
ES modules require HTTP; opening the HTML through `file://` is unsupported.
No npm installation, CDN or build step is needed. `PORT` overrides the demo port.
The server listens on loopback. Demo arrivals are synthetic and never control a queue.

## Contract

```javascript
import './src/job-queue-timeline.js';
const viewer = document.querySelector('job-queue-timeline');
viewer.setData({
  queues: [{ id: 'parallel', name: 'ParallelQ', maxParallelism: 3 }],
  jobs: [{ id: 'job-1', queueId: 'parallel', channel: 2, name: 'Normalize',
    observedAt: '2026-09-17T12:00:00.000Z', state: 'running', dependsOn: [],
    description: '', rootJobId: null, durationMs: null, metadata: {}, isRoot: false }]
});
viewer.setReferenceTime('2026-09-17T12:00:00.000Z');
viewer.addEventListener('job-select', event => console.log(event.detail.jobId));
```

Give the element an explicit height. Snapshots replace data atomically. Invalid
IDs, timestamps, queue references and channels are rejected before changing the
view. IDs must be nonempty strings; timestamps must specify a timezone.
`channel` is required and is either null or an integer in `0..maxParallelism-1`.
Null jobs occupy an explicitly labeled **Unassigned** area outside the numbered
execution channels. Nothing maps null to channel zero.

The optional `IJobExecutionEvent` interface supplies the sample's runtime channel.
Existing `IJobEvent` implementations remain valid and project as unassigned.
Terminal snapshots retain their captured channel after capacity is released.
Root identity never controls channel placement. Only `dependsOn` creates edges;
missing endpoints remain unresolved. State aliases `queued` and `canceled`
normalize to `waiting` and `cancelled`; unsupported states display as `unknown`.

The browser receives presentation values only. Metadata is bounded to 12 scalar
entries, keys to 48 characters and values to 160; structured values are summarized.
Text is rendered with textContent, never inserted as HTML.

| API | Behavior |
| --- | --- |
| `setData(snapshot)` | Validate, detach and replace the current snapshot |
| `configure(options)` | Update presentation settings; never reconfigure a runtime queue |
| `setReferenceTime(ISO or epoch milliseconds)` | Set the window end and enter historical mode |
| `followLive()` | Return to five seconds and follow clock minus configured `liveLagMs` (default zero) |
| `showOverview()` | Return to five seconds; restore the time and live/history mode saved before inspection |
| `focusJob(id)` | Search the actual dependency graph in both directions; select and focus |
| `clearFocus()` | Remove selection and graph emphasis |
| `selectedJobId`, `referenceTime`, `isFollowingLive`, `visibleWindowMs` | Read-only controller state |
| `job-select` | Bubbling, composed event containing jobId and rootJobId |

## Review map

| File | Responsibility |
| --- | --- |
| `src/model.js` | Validation, detachment and normalized presentation state |
| `src/graph.js` | Search and dependency traversal |
| `src/config.js` | Geometry defaults and readable minimums |
| `src/layout.js` | Exact coordinates, time ticks, channel columns, density grouping and straight edge paths |
| `src/job-queue-timeline.js` | Component/controller, clock, input and lifecycle |
| `src/template.js`, `src/styles.css` | Accessible controls and centralized theme tokens |
| `src/renderer/svg-renderer.js` | Mechanical SVG DOM adapter |
| `integrations/blazor/job-monitor.js` | Idempotent selection subscription and snapshot bridge |

Queue width respects minimum channel width even after resizing. Horizontal
overflow scrolls. History can be changed with the native vertical scrollbar,
wheel, reference datetime input, or arrow/PageUp/PageDown keys on the viewport.
Queue dividers support dragging and left/right arrow keys. Search results and
job nodes are keyboard accessible. Hover provides bounded metadata; job focus
also announces metadata through an accessible status region.

## Timing and limits

The overview displays the five seconds ending at the reference time, with older
jobs above and the reference at the bottom. Live following advances that window;
Pause freezes it. History navigation preserves the interval. The UTC time ruler
stays on the left during horizontal scrolling, with adaptive readable tick spacing
(normally 500 ms in a sufficiently tall five-second view).

Each job has a square centered exactly on its timestamp and channel. Its side is
2 CSS pixels in the overview and grows proportionally with time magnification,
up to 12 pixels. A 24-pixel outline/hit area and state symbol provide a larger
visual target without changing the center. Hover, keyboard focus and selection
highlight the channel and exact UTC time on the ruler. Subpixel coordinates retain
the time mapping; a five-second overview does not resolve every millisecond into
a separate physical display pixel.

Default lane pitch is 40 pixels, independent of time zoom, with 8-pixel queue
padding: three assigned channels use 136 pixels. Unassigned adds another column,
marked U with an explicit tooltip and accessible label. Resizing cannot violate
the minimum pitch. Theme values remain configurable through CSS variables.

Jobs whose same-lane targets would overlap are represented by one distinct
28-pixel count marker with a bracket spanning the group's actual time range.
Grouping includes an 8-pixel gap and never shifts individual timestamps.
Select a count marker by mouse or Enter/Space to inspect a shorter interval in
the same viewer. A temporary, scrollable list permits individual selection,
including jobs at identical times that no zoom can separate. Back to 5 seconds
or Escape restores the previous overview. Closing the list retains the current
inspection interval. Follow live always returns to the five-second overview.

The +/- controls magnify time only, around the visible interval's midpoint.
`configure({ scale })` uses 1 for the overview and clamps magnification to a
minimum one-millisecond window. `windowMs` may explicitly set a shorter base
interval, up to 5000 ms. Legacy pixelsPerSecond/rowGap/minNodeRadius settings no
longer control geometry; use the window, marker and channel settings instead.
Selection centers the selected job in the current interval, highlights its actual
dependency graph and does not widen the interval to fit the entire graph.

Dependency segments are straight. Edges between grouped jobs and other markers
terminate at the group marker and describe membership, not one precise event
time; internal group edges are hidden until their endpoints separate. Endpoints
outside the visible interval are not drawn. General edge crossings remain possible.

The demo includes three queue types, a 15-channel queue, a root with three
dependencies, cross-queue edges, dense same-channel points and all requested states.
Large graphs remain scrollable; general edge crossings are possible. Snapshots
are intended to be bounded by the host; this is not a durable event history or an
unlimited graph renderer. No incremental append API is provided.

## Blazor integration

See the maintained [sample architecture](../../docs/architecture/blazor-consumer-sample.md).
The sample build synchronizes `src/`, `integrations/blazor/` and `LICENSE` into
the ignored `wwwroot/job-monitor` directory before static asset discovery.
Build and publish copy those assets deterministically.
There is one maintained source tree, not two edited asset copies.
The Debug build additionally serves `/job-monitor/tests/blazor.html` to check
the actual circuit, selection and navigation away/back; that harness is excluded
from publishing.

The earlier `FIXES_2026-09-17.md` records historical validation, not current results.
Current commands and results belong in the implementation report. MIT licensed.
