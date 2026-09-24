import test from 'node:test';
import assert from 'node:assert/strict';
import { normalize } from '../src/model.js';
import { layout } from '../src/layout.js';
import { connected, search } from '../src/graph.js';
import { options } from '../src/config.js';

const time = '2026-09-17T12:00:00.000Z';
const job = (id, channel, extra = {}) => ({ id, queueId: 'q', channel, name: id,
  observedAt: time, state: 'running', dependsOn: [], ...extra });
const data = jobs => ({ queues: [{ id: 'q', name: 'Parallel', maxParallelism: 3 }], jobs });

test('shared memberships are detached and graph focus preserves every dependency for all outcomes', () => {
  for (const state of ['running', 'failed', 'cancelled']) {
    const roots = ['first', 'second'];
    const model = normalize(data([
      job('shared', 2, { rootJobIds: roots, state }),
      job('first', null, { rootJobIds: ['first'], isRoot: true, state, dependsOn: ['shared'] }),
      job('second', 0, { rootJobIds: ['second'], isRoot: true, state, dependsOn: ['shared'] }),
      job('unrelated', 1)
    ]));
    roots.push('later');
    assert.deepEqual(model.jobs[0].rootJobIds, ['first', 'second']);
    assert.ok(Object.isFrozen(model.jobs[0].rootJobIds));
    assert.equal(model.jobs[0].rootJobId, null);
    assert.equal(model.jobs[0].channel, 2);
    for (const selected of ['shared', 'first', 'second'])
      assert.deepEqual(connected(model, selected), new Set(['shared', 'first', 'second']));
  }
});
test('membership accepts legacy singular IDs and rejects malformed lists', () => {
  assert.deepEqual(normalize(data([job('a', null, { rootJobId: 'r' })])).jobs[0].rootJobIds, ['r']);
  for (const rootJobIds of ['r', [null], ['']])
    assert.throws(() => normalize(data([job('a', null, { rootJobIds })])), /rootJobIds|ID/);
});

test('explicit channels survive root grouping; null remains unassigned', () => {
  const model = normalize(data([job('a', 2, { rootJobId: 'r' }), job('b', null)]));
  assert.equal(model.jobs[0].channel, 2);
  assert.equal(model.jobs[1].channel, null);
  const view = layout(model, { referenceTime: Date.parse(time), height: 600 });
  assert.equal(view.queues[0].channels.length, 3);
  assert.equal(view.nodes.find(n => n.id === 'b').unassigned, true);
});
test('missing, negative, fractional and out-of-range channels are rejected', () => {
  for (const channel of [undefined, -1, 1.5, 3, '0'])
    assert.throws(() => normalize(data([job('a', channel)])), /channel/i);
});
test('states normalize and unsupported input stays visible', () => {
  const model = normalize(data([job('a', 0, { state: 'canceled' }), job('b', 1, { state: 'mystery' })]));
  assert.equal(model.jobs[0].state, 'cancelled');
  assert.equal(model.jobs[1].state, 'unknown');
});
test('same-time jobs aggregate without changing their exact positions', () => {
  const jobs = Array.from({ length: 12 }, (_, i) => job(`j${i}`, 0));
  const view = layout(normalize(data(jobs)), { referenceTime: Date.parse(time), height: 600, scale: .1 });
  assert.equal(view.clusters.length, 1);
  assert.equal(view.clusters[0].members.length, 12);
  assert.equal(view.markers.length, 1);
  assert.ok(view.nodes.every(n => n.y === n.trueY && n.y === view.referenceY));
  assert.ok(view.nodes.every(n => n.channel === 0 && n.observedAt === time));
});
test('different channels can show simultaneous executions without reassignment', () => {
  const view = layout(normalize(data([job('a', 0), job('b', 2)])), { referenceTime: Date.parse(time), height: 600 });
  assert.equal(view.nodes[0].y, view.nodes[1].y);
  assert.notEqual(view.nodes[0].x, view.nodes[1].x);
});
test('dense queue keeps fifteen channels and readable minimum width', () => {
  const model = normalize({ queues: [{ id: 'q', name: 'Dense', maxParallelism: 15 }], jobs: [] });
  const view = layout(model, { referenceTime: Date.parse(time), height: 600, widths: { q: 1 } });
  assert.equal(view.queues[0].channels.length, 15);
  assert.equal(view.queues[0].width, 15 * 40 + 16);
});

test('five-second window fits the available height and a three-channel queue is compact', () => {
  const end = Date.parse(time);
  const model = normalize(data([job('start', 0, { observedAt: new Date(end - 5000).toISOString() }), job('end', 1)]));
  for (const height of [400, 800]) {
    const view = layout(model, { referenceTime: end, height });
    assert.equal(view.windowMs, 5000);
    assert.equal(view.nodes[0].y, view.plotTop);
    assert.equal(view.nodes[1].y, view.referenceY);
    assert.equal(view.queues[0].width, 136);
    assert.ok(view.ticks.length > 2);
  }
});

test('zoom separates close jobs while scaling squares around true centers', () => {
  const end = Date.parse(time);
  const model = normalize(data([job('a', 0), job('b', 0, { observedAt: new Date(end - 10).toISOString() })]));
  const overview = layout(model, { referenceTime: end });
  const detail = layout(model, { referenceTime: end, scale: 100 });
  assert.equal(overview.clusters.length, 1);
  assert.equal(detail.clusters.length, 0);
  assert.equal(overview.markerSize, 2);
  assert.equal(detail.markerSize, 12);
  assert.equal(detail.queues[0].width, overview.queues[0].width);
  assert.ok(detail.nodes.every(n => n.y === n.trueY));
  assert.equal(detail.nodes[0].y - detail.nodes[1].y, 10 * detail.pixelsPerMs);
});

test('clusters are deterministic, lane-local and do not overlap other markers', () => {
  const end = Date.parse(time);
  const jobs = [job('a', 0), job('b', 0), job('c', 1), job('waiting', null),
    job('older', 0, { observedAt: new Date(end - 1000).toISOString() })];
  const view = layout(normalize(data(jobs)), { referenceTime: end });
  const reversed = layout(normalize(data([...jobs].reverse())), { referenceTime: end });
  assert.deepEqual(view.clusters[0].members.map(n => n.id), ['a', 'b']);
  assert.equal(view.clusters[0].id, reversed.clusters[0].id);
  assert.equal(view.markers.length, 3); // Unassigned is collapsed by default.
  const sameLane = view.markers.filter(n => n.channel === 0).sort((a,b) => a.y - b.y);
  assert.ok(sameLane[1].y - sameLane[0].y >= 32);
});

test('visible window excludes outside jobs from clusters and edges are straight', () => {
  const end = Date.parse(time);
  const model = normalize(data([job('a', 0), job('b', 1, { dependsOn: ['a'] }),
    job('outside', 0, { observedAt: new Date(end + 1).toISOString() })]));
  const view = layout(model, { referenceTime: end });
  assert.equal(view.markers.length, 2);
  assert.equal(view.clusters.length, 0);
  assert.match(view.edges[0].path, /^M .+ L /);
  assert.doesNotMatch(view.edges[0].path, /[CQ]/);
});

test('identical times stay grouped at maximum zoom and internal edges are hidden', () => {
  const view = layout(normalize(data([job('a', 0), job('b', 0, { dependsOn: ['a'] })])),
    { referenceTime: Date.parse(time), scale: 5000 });
  assert.equal(view.clusters.length, 1);
  assert.equal(view.edges.length, 0);
  assert.equal(view.clusters[0].startTime, view.clusters[0].endTime);
});

test('invalid geometry is rejected and zoom respects its readable bounds', () => {
  for (const input of [{ scale: NaN }, { windowMs: Infinity }, { minMarkerSize: -1 }, { markerGap: '8' }])
    assert.throws(() => options(input), /Invalid option/);
  const small = options({ scale: 0, minMarkerSize: 0, minChannelWidth: 1 });
  assert.equal(small.scale, 1);
  assert.equal(small.minMarkerSize, 2);
  assert.equal(small.minChannelWidth, 40);
  assert.equal(options({ scale: 1e6 }).scale, 5000);
});

test('ruler formats timezone offsets in UTC and empty snapshots still have ticks', () => {
  const model = normalize(data([job('offset', 0, { observedAt: '2026-09-17T14:00:00.001+02:00' })]));
  const view = layout(model, { referenceTime: Date.parse(time) + 1000 });
  assert.equal(view.nodes[0].timeLabel, '12:00:00.001');
  const empty = layout(normalize({ queues: [], jobs: [] }), { referenceTime: Date.parse(time) });
  assert.equal(empty.markers.length, 0);
  assert.ok(empty.ticks.length > 2);
});
test('time increases downwards and advancing reference moves nodes upward', () => {
  const model = normalize(data([job('a', 0)]));
  const earlier = layout(model, { referenceTime: Date.parse(time) - 1000, height: 600 });
  const later = layout(model, { referenceTime: Date.parse(time) + 1000, height: 600 });
  assert.ok(earlier.nodes[0].y > earlier.referenceY);
  assert.ok(later.nodes[0].y < later.referenceY);
  assert.equal(earlier.nodes[0].x, later.nodes[0].x);
});
test('search matches name ID description and graph traversal follows only actual edges', () => {
  const model = normalize(data([job('a', 0, { description: 'sensor' }), job('b', 1, { dependsOn: ['a'] }),
    job('c', 2, { rootJobId: 'a' })]));
  assert.equal(search(model, 'SENSOR')[0].id, 'a');
  assert.deepEqual([...connected(model, 'b')].sort(), ['a', 'b']);
});
test('cross-queue edges resolve by ID; missing endpoints are not fabricated', () => {
  const model = normalize({ queues: [{ id: 'q', name: 'Q', maxParallelism: 1 }, { id: 'r', name: 'R', maxParallelism: 1 }],
    jobs: [job('a', 0), job('b', 0, { queueId: 'r', dependsOn: ['a', 'missing'] })] });
  const view = layout(model, { referenceTime: Date.parse(time), height: 600 });
  assert.equal(view.edges.length, 1);
  assert.equal(view.missingEdges, 1);
});
test('normalization detaches metadata and rejects duplicate IDs and timezone-free timestamps', () => {
  const input = data([job('a', 0, { metadata: { safe: 'before' } })]);
  const model = normalize(input);
  input.jobs[0].metadata.safe = 'after';
  assert.match(model.jobs[0].metadataText, /before/);
  assert.throws(() => normalize(data([job('a', 0), job('a', 1)])), /duplicate/i);
  assert.throws(() => normalize(data([job('a', 0, { observedAt: '2026-09-17T12:00:00' })])), /timestamp/i);
});

test('unassigned is a separate collapsible strip without moving assigned channel centers', () => {
  const model = normalize(data([job('assigned', 0), job('waiting', null, { state: 'waiting' })]));
  const collapsed = layout(model, { referenceTime: Date.parse(time) });
  const expanded = layout(model, { referenceTime: Date.parse(time), expandedUnassigned: new Set(['q']) });
  assert.equal(collapsed.queues[0].unassignedCount, 1);
  assert.equal(collapsed.queues[0].unassignedExpanded, false);
  assert.equal(collapsed.markers.length, 1);
  assert.equal(expanded.markers.length, 2);
  assert.equal(expanded.queues[0].unassignedExpanded, true);
  assert.equal(expanded.queues[0].unassignedWidth, expanded.queues[0].slotWidth);
  assert.ok(expanded.queues[0].width > collapsed.queues[0].width);
  assert.deepEqual(collapsed.queues[0].channels, expanded.queues[0].channels);
  assert.ok(expanded.nodes.find(n => n.id === 'waiting').x > expanded.queues[0].channels.at(-1).x);
  assert.equal(expanded.nodes.find(n => n.id === 'waiting').channel, null);
});

test('a backend waiting-to-started update removes the waiting marker and uses the new time', () => {
  const queued = normalize(data([job('a', null, { state: 'waiting' })]));
  const started = normalize(data([job('a', 1, { observedAt: '2026-09-17T12:00:01.000Z' })]));
  const before = layout(queued, { referenceTime: Date.parse(time) + 2000, expandedUnassigned: new Set(['q']) });
  const after = layout(started, { referenceTime: Date.parse(time) + 2000, expandedUnassigned: new Set(['q']) });
  assert.equal(before.queues[0].unassignedCount, 1);
  assert.equal(after.queues[0].unassignedCount, 0);
  assert.equal(after.markers.length, 1);
  assert.equal(after.markers[0].channel, 1);
  assert.ok(after.markers[0].y > before.markers[0].y);
});

test('enqueue history survives assignment with exact positions and a directed relation', () => {
  const end = Date.parse(time) + 2000;
  const input = data([job('a', 1, { observedAt: new Date(end - 1000).toISOString(), enqueuedAt: time })]);
  const model = normalize(input);
  const view = layout(model, { referenceTime: end, expandedUnassigned: new Set(['q']) });
  const enqueue = view.nodes.find(n => n.phase === 'enqueue');
  const execution = view.nodes.find(n => n.phase === 'execution');
  assert.equal(model.jobs.length, 1);
  assert.equal(view.queues[0].unassignedCount, 1);
  assert.equal(view.markers.length, 2);
  assert.equal(enqueue.id, execution.id);
  assert.notEqual(enqueue.markerId, execution.markerId);
  assert.equal(enqueue.channel, null);
  assert.equal(enqueue.state, 'waiting');
  assert.equal(enqueue.time, Date.parse(time));
  assert.equal(execution.channel, 1);
  assert.ok(Math.abs(execution.y - enqueue.y - 1000 * view.pixelsPerMs) < 1e-9);
  assert.equal(view.edges.length, 1);
  assert.equal(view.edges[0].kind, 'assignment');
  assert.equal(view.edges[0].fromMarker, enqueue.markerId);
  assert.equal(view.edges[0].toMarker, execution.markerId);
  assert.match(view.edges[0].path, /^M .+ L /);
  // Fresh snapshots carry the history; no browser-side event cache is required.
  assert.deepEqual(layout(normalize(input), { referenceTime: end, expandedUnassigned: new Set(['q']) }), view);
});

test('assignment arrow ends outside the job outline so its direction stays visible', () => {
  const model = normalize(data([job('a', 0, { enqueuedAt: time })]));
  const view = layout(model, { referenceTime: Date.parse(time), expandedUnassigned: new Set(['q']) });
  const execution = view.nodes.find(n => n.phase === 'execution');
  const endpoint = view.edges[0].path.split(' L ')[1].split(' ').map(Number);
  assert.equal(endpoint[0], execution.x + execution.targetSize / 2);
  assert.equal(endpoint[1], execution.y);
});

test('waiting and legacy jobs are not duplicated; missing enqueue times are not invented', () => {
  for (const extra of [{}, { enqueuedAt: null }, { enqueuedAt: time }]) {
    const waiting = layout(normalize(data([job('a', null, extra)])), { referenceTime: Date.parse(time) });
    assert.equal(waiting.nodes.length, 1);
    assert.equal(waiting.edges.length, 0);
  }
  const assigned = layout(normalize(data([job('a', 0)])), { referenceTime: Date.parse(time) });
  assert.equal(assigned.nodes.length, 1);
  assert.equal(assigned.queues[0].unassignedCount, 0);
  for (const enqueuedAt of ['', 'invalid', '2026-09-17T12:00:00', 1])
    assert.throws(() => normalize(data([job('a', 0, { enqueuedAt })])), /timestamp/i);
});

test('enqueue groups retain assignment relations without changing dependency endpoints', () => {
  const model = normalize(data([job('a', 0, { enqueuedAt: time }),
    job('b', 1, { enqueuedAt: time, dependsOn: ['a', 'missing'] })]));
  const view = layout(model, { referenceTime: Date.parse(time), expandedUnassigned: new Set(['q']) });
  assert.equal(view.clusters.length, 1);
  assert.equal(view.clusters[0].channel, null);
  assert.equal(view.edges.filter(e => e.kind === 'assignment').length, 2);
  const dependency = view.edges.find(e => e.kind === 'dependency');
  assert.equal(dependency.fromMarker, view.nodes.find(n => n.id === 'a' && n.channel === 0).markerId);
  assert.equal(dependency.toMarker, view.nodes.find(n => n.id === 'b' && n.channel === 1).markerId);
  assert.equal(view.missingEdges, 1);
  assert.ok(view.edges.every(e => !/NaN|Infinity/.test(e.path)));
  const collapsed = layout(model, { referenceTime: Date.parse(time) });
  assert.equal(collapsed.queues[0].unassignedCount, 2);
  assert.equal(collapsed.edges.length, 1);
  assert.equal(collapsed.edges[0].kind, 'dependency');
});

test('enqueue-to-start relations require both endpoints in the visible interval', () => {
  const model = normalize(data([job('a', 0, { enqueuedAt: '2026-09-17T11:59:54.000Z' })]));
  const view = layout(model, { referenceTime: Date.parse(time), expandedUnassigned: new Set(['q']) });
  assert.equal(view.queues[0].unassignedCount, 1);
  assert.equal(view.queues[0].unassignedVisibleCount, 0);
  assert.equal(view.markers.length, 1);
  assert.equal(view.edges.length, 0);
});
