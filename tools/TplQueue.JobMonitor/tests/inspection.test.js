import test from 'node:test';
import assert from 'node:assert/strict';
import { normalize, positions } from '../src/model.js';
import { layout } from '../src/layout.js';
import { inspectionWindow, groupWindow, groupPlotHeight, summarize } from '../src/inspection.js';
import { neighborhood } from '../src/graph.js';

const time = Date.parse('2026-10-12T12:00:00Z');
const job = (id, channel, offset = 0, extra = {}) => ({ id, channel, queueId: 'q',
  observedAt: new Date(time + offset).toISOString(), state: 'completed', ...extra });
const model = jobs => normalize({ queues: [{ id: 'q', maxParallelism: 2 }], jobs });

test('member inspection separates distinct milliseconds without changing true positions', () => {
  const data = model([job('a', 0), job('b', 0, 1), job('c', 0, 2)]);
  const points = positions(data), selected = points[1];
  const window = inspectionWindow(points, selected, 5000, 146, 36);
  const view = layout(data, { height: 240, referenceTime: selected.time + window / 2, scale: 5000 / window });
  assert.ok(view.markers.some(n => n.id === 'b' && n.kind === 'job'));
  assert.equal(view.nodes.find(n => n.id === 'b').time, time + 1);
  assert.equal(inspectionWindow(points, selected, 1, 146, 36), 1);
});

test('equal times remain grouped and expose internal dependencies and current state', () => {
  const data = model([job('a', 0, 0, { isRoot: true, rootJobIds: ['a'] }),
    job('b', 0, 0, { isRoot: true, rootJobIds: ['b'], dependsOn: ['a'] })]);
  const view = layout(data, { referenceTime: time, scale: 5000 });
  assert.equal(view.clusters[0].internalDependencies, 1);
  assert.equal(view.clusters[0].summary.jobCount, 2);
  assert.equal(view.clusters[0].summary.rootCount, 2);
  assert.equal(view.edges.length, 0);
  assert.match(view.clusters[0].description, /1 internal dependenc/);
});

test('group connectors bundle real relationships and retain both endpoint IDs', () => {
  const data = model([job('a', 0), job('b', 0), job('c', 1, 0, { dependsOn: ['a', 'b'] }),
    job('d', 1, 0, { dependsOn: ['b'] })]);
  const view = layout(data, { referenceTime: time });
  assert.equal(view.edges.length, 1);
  assert.equal(view.edges[0].count, 3);
  assert.deepEqual(view.edges[0].relationships.map(r => [r.from, r.to]), [['a', 'c'], ['b', 'c'], ['b', 'd']]);
  assert.match(view.edges[0].description, /3 dependencies/);
});

test('root focus includes composed members and immediate neighbors without traversing the FIFO chain', () => {
  const data = model(['a', 'b', 'c', 'd', 'e'].map((id, i, ids) => job(id, 0, i * 100,
    { isRoot: true, rootJobIds: [id], dependsOn: i ? [ids[i - 1]] : [] }))
    .concat(job('member', 1, 0, { rootJobIds: ['c'] })));
  assert.deepEqual(neighborhood(data, 'c'), new Set(['c', 'member', 'b', 'd']));
  assert.deepEqual(neighborhood(data, 'absent'), new Set());
});

test('enqueue history never counts completed jobs as current waiting work', () => {
  const data = model([job('done', 0, 10, { enqueuedAt: new Date(time).toISOString() }),
    job('waiting', null, 0, { state: 'waiting' })]);
  const view = layout(data, { referenceTime: time + 20, expandedUnassigned: new Set(['q']) });
  assert.equal(view.queues[0].waitingCount, 1);
  assert.equal(view.queues[0].historyCount, 1);
  const summary = summarize(positions(data));
  assert.equal(summary.jobCount, 2);
  assert.equal(summary.states.completed, 1);
  assert.equal(summary.states.waiting, 1);
});

test('inspection fits every group member, including groups longer than the overview', () => {
  for (const offsets of [[0, 1, 40], [0, 0], [0, 8000, 12000]]) {
    const data = model(offsets.map((offset, i) => job(`member-${i}`, 0, offset)));
    const range = groupWindow(positions(data));
    const view = layout(data, { referenceTime: range.referenceTime, windowMs: range.windowMs });
    const represented = view.markers.flatMap(n => n.members ?? [n]).map(n => n.id).sort();
    assert.deepEqual(represented, data.jobs.map(j => j.id).sort());
    assert.ok(view.nodes.every(n => n.time >= view.startTime && n.time <= view.referenceTime));
  }
});

test('group inspection gives every distinct timestamp enough vertical space without cropping members', () => {
  for (const offsets of [[0, 1, 40], Array.from({ length: 50 }, (_, i) => i), [0, 0, 1]]) {
    const data = model(offsets.map((offset, i) => job(`member-${i}`, 0, offset)));
    const points = positions(data), range = groupWindow(points);
    const plotHeight = groupPlotHeight(points, range.windowMs, 146, 36);
    const view = layout(data, { height: plotHeight + 94, referenceTime: range.referenceTime,
      windowMs: Math.max(5000, range.windowMs), scale: Math.max(5000, range.windowMs) / range.windowMs });
    assert.deepEqual(view.markers.flatMap(n => n.members ?? [n]).map(n => n.id).sort(), data.jobs.map(j => j.id).sort());
    assert.ok(view.clusters.every(n => n.members.every(j => j.time === n.startTime)), 'only identical times remain grouped');
    assert.ok(view.nodes.every(n => n.y === n.trueY));
  }
  assert.equal(groupPlotHeight([], 5000, 146, 36), 146);
  assert.equal(groupPlotHeight(positions(model([job('a', 0), job('b', 1, 1)])), 5000, 146, 36), 146,
    'different channels do not force vertical expansion');
});

test('groups remain present and turn green only when every member completes', () => {
  for (const state of ['waiting', 'running', 'retried', 'failed', 'cancelled', 'unknown', 'completed']) {
    const view = layout(model([job('a', 0), job('b', 0, 1, { state })]), { referenceTime: time + 20 });
    assert.equal(view.clusters.length, 1);
    assert.equal(view.clusters[0].label, '2');
    assert.equal(view.clusters[0].state, state === 'completed' ? 'completed' : 'incomplete');
  }
  const data = model([job('a', 0, 100, { enqueuedAt: new Date(time).toISOString() }),
    job('b', 1, 200, { enqueuedAt: new Date(time + 1).toISOString() })]);
  const view = layout(data, { referenceTime: time + 300, expandedUnassigned: new Set(['q']) });
  assert.equal(view.clusters.find(n => n.channel === null).state, 'completed', 'enqueue groups use current job states');
});

