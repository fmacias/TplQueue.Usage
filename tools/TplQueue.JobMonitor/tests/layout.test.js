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
  assert.equal(view.markers.length, 4);
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
