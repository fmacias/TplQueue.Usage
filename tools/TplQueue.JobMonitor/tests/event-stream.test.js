import test from 'node:test';
import assert from 'node:assert/strict';
import { projectEventStream } from '../src/event-stream.js';
import { normalize, positions } from '../src/model.js';

const queues = [{ id: 'parallel', queueId: 'queue-1', name: 'ParallelQ', maxParallelism: 3 }];
const time = seconds => `2026-10-21T10:00:${String(seconds).padStart(2, '0')}.000Z`;
const event = (status, seconds, executionChannel = null, extra = {}) => ({
  status, timestamp: time(seconds), executionChannel, retryCount: 0, diagnostic: { hasException: false },
  jobInfo: { id: 'job', name: 'Test job', crossQueueId: 'queue-1', dependencies: [] }, ...extra
});
const stream = (events, rootJobIds = {}) => ({ queues, events, rootJobIds });
const job = events => projectEventStream(stream(events)).jobs[0];

test('direct snapshots preserve repeated occurrences separately from visual state', () => {
  const repeated = event(5, 2, 1, { retryCount: 1 });
  const input = stream([event(2, 0), event(4, 1, 1), repeated, structuredClone(repeated), event(6, 3, 1), event(9, 4, 1)]);
  const before = structuredClone(input);
  const result = projectEventStream(input);
  assert.equal(result.jobs.length, 1);
  assert.equal(result.jobs[0].state, 'completed');
  assert.equal(result.jobs[0].metadata.retryCount, '1');
  assert.deepEqual(input, before);
  assert.equal(input.events.length, 6);
});

for (const [status, state] of [[5, 'running'], [6, 'completed'], [7, 'cancelled'], [8, 'failed']]) {
  test(`status ${status} requires a matching Started event and survives late enrichment`, () => {
    const events = [event(2, 0), event(status, 3, 2)];
    assert.equal(job(events).channel, null);
    const result = job([...events, event(4, 1, 2), event(2, 0)]);
    assert.equal(result.channel, 2);
    assert.equal(result.observedAt, time(1));
    assert.equal(result.enqueuedAt, time(0));
    assert.equal(result.state, state);
    const model = normalize(projectEventStream(stream([...events, event(4, 1, 2)])));
    assert.equal(positions(model).length, 2);
  });
}

test('terminal channel cannot be relocated by later observations on a different channel', () => {
  const result = job([event(4, 1, 1), event(6, 2, 1), event(4, 3, 2), event(5, 4, 2), event(9, 5, 2)]);
  assert.equal(result.channel, 1);
  assert.equal(result.observedAt, time(1));
  assert.equal(result.state, 'completed');
});

test('an active channel change needs its own Started observation', () => {
  const events = [event(4, 0, 1), event(5, 2, 2)];
  assert.equal(job(events).channel, null);
  assert.equal(job([...events, event(4, 1, 2)]).observedAt, time(1));
});

test('sequential jobs sharing enqueue time retain distinct start times', () => {
  const second = e => ({ ...e, jobInfo: { ...e.jobInfo, id: 'second' } });
  const result = projectEventStream(stream([event(2, 0), second(event(2, 0)), event(4, 1, 0), second(event(4, 2, 0))]));
  assert.deepEqual(result.jobs.map(j => j.observedAt), [time(1), time(2)]);
});

test('shared membership does not create unobserved jobs, channels or dependency edges', () => {
  const input = stream([event(5, 2)], { job: ['root-b', 'root-a'] });
  const result = projectEventStream(input);
  assert.equal(result.jobs.length, 1);
  assert.deepEqual(result.jobs[0].rootJobIds, ['root-a', 'root-b']);
  assert.equal(result.jobs[0].rootJobId, null);
  assert.equal(result.jobs[0].channel, null);
  assert.deepEqual(result.jobs[0].dependsOn, []);
});

test('dependencies remain explicit and root completion cannot complete a prerequisite', () => {
  const child = event(5, 1, 1);
  const root = event(9, 3, 0, { jobInfo: { id: 'root', name: 'Root', crossQueueId: 'queue-1', dependencies: [child.jobInfo] } });
  const result = projectEventStream(stream([child, root], { job: ['root'], root: ['root'] }));
  assert.equal(result.jobs.find(j => j.id === 'job').state, 'running');
  assert.deepEqual(result.jobs.find(j => j.id === 'root').dependsOn, ['job']);
});

test('old snapshots stay detached and replacement eviction removes visual jobs', () => {
  const first = projectEventStream(stream([event(4, 1, 0)]));
  const next = projectEventStream(stream([]));
  assert.equal(first.jobs[0].channel, 0);
  assert.deepEqual(next.jobs, []);
  assert.equal(first.jobs[0].enqueuedAt, null);
});

test('validates queue, timestamp and channel even for an old or terminal observation', () => {
  for (const invalid of [event(5, 2, 3), event(5, 2, -1), event(5, 2, 0, { timestamp: 'invalid' }),
    event(5, 2, 0, { jobInfo: { id: 'job', crossQueueId: 'unknown', dependencies: [] } })]) {
    assert.throws(() => projectEventStream(stream([event(6, 3, 1), invalid])), TypeError);
  }
});

test('numeric and named enum transport agree, including retry and unknown states', () => {
  assert.equal(job([event('Running', 1, null, { retryCount: 2 })]).state, 'retried');
  assert.equal(job([event('Requeuing', 1)]).state, 'retried');
  assert.equal(job([event(1234, 1)]).state, 'unknown');
  assert.equal(job([event('Canceled', 1)]).state, 'cancelled');
});

test('root-success fallback preserves FIFO root boundaries without synthesizing observations', () => {
  const first = event(9, 2, 0, { jobInfo: { id: 'first', name: 'First', crossQueueId: 'queue-1', dependencies: [] } });
  const second = event(9, 3, 0, { jobInfo: { id: 'second', name: 'Second', crossQueueId: 'queue-1',
    dependencies: [first.jobInfo, { id: 'unobserved', dependencies: [] }] } });
  const result = projectEventStream(stream([second, first]));
  assert.equal(result.jobs.length, 2);
  assert.deepEqual(result.jobs.find(j => j.id === 'first').rootJobIds, ['first']);
  assert.deepEqual(result.jobs.find(j => j.id === 'second').rootJobIds, ['second']);
});

test('queue-local channel zero does not merge jobs or memberships across queues', () => {
  const input = stream([event(4, 1, 0), event(4, 2, 0, {
    jobInfo: { id: 'other', name: 'Other', crossQueueId: 'queue-2', dependencies: [] }
  })]);
  input.queues = [...queues, { id: 'fifo', queueId: 'queue-2', name: 'FIFO', maxParallelism: 1 }];
  const result = normalize(projectEventStream(input));
  assert.deepEqual(result.jobs.map(j => [j.queueId, j.channel]), [['parallel', 0], ['fifo', 0]]);
});

test('duration is based on lifecycle observations and diagnostics never enter renderer metadata', () => {
  const result = job([event(3, 1), event(4, 2, 0), event(8, 4, 0, { diagnostic: { hasException: true, message: 'private detail' } })]);
  assert.equal(result.durationMs, 3000);
  assert.equal(result.metadata.durationSource, 'observer lifecycle timestamps');
  assert.equal(result.observedAt, time(2));
  assert.ok(!JSON.stringify(result).includes('private detail'));
});
