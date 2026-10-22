import { timestamp } from './model.js';

// JobEventStatus wire values. Accept enum names as well as System.Text.Json's default numeric values.
const statuses = ['Cache', 'Enqueueing', 'Enqueued', 'Dequeued', 'Started', 'Running',
  'Successed', 'Canceled', 'Failed', 'RootSuccessed', 'Requeuing'];
const queued = new Set(['Cache', 'Enqueueing', 'Enqueued']);
const running = new Set(['Dequeued', 'Started', 'Running']);
const terminal = new Set(['Successed', 'RootSuccessed', 'Canceled', 'Failed']);
const rank = status => terminal.has(status) ? 3 : running.has(status) || status === 'Requeuing' ? 2 : queued.has(status) ? 1 : 0;
const state = (status, retries) => {
  if (status === 'Successed' || status === 'RootSuccessed') return 'completed';
  if (status === 'Canceled') return 'cancelled';
  if (status === 'Failed') return 'failed';
  if (status === 'Requeuing' || running.has(status) && retries > 0) return 'retried';
  return running.has(status) ? 'running' : queued.has(status) ? 'waiting' : 'unknown';
};
const earlier = (current, next) => current === null || timestamp(next) < timestamp(current) ? next : current;
const later = (current, next) => current === null || timestamp(next) > timestamp(current) ? next : current;
const requiredId = value => {
  if (typeof value !== 'string' || !value.trim()) throw new TypeError('An ID is required');
  return value;
};

// Producers without a composition catalog retain success-based membership, but never create jobs
// from nested metadata. A separately observed root is a boundary even when FIFO adds an edge to it.
function rootMembership(snapshot, jobs) {
  const memberships = new Map();
  if ([...jobs.keys()].every(id => Object.hasOwn(snapshot.rootJobIds ?? {}, id))) return memberships;
  const roots = snapshot.events.filter(e => e.status === 9 || e.status === 'RootSuccessed');
  const rootIds = new Set(roots.map(e => e.jobInfo.id));
  for (const root of roots) {
    const pending = [root.jobInfo], seen = new Set();
    while (pending.length) {
      const info = pending.pop();
      if (seen.has(info.id) || info.id !== root.jobInfo.id && rootIds.has(info.id)) continue;
      seen.add(info.id);
      if (jobs.has(info.id)) {
        if (!memberships.has(info.id)) memberships.set(info.id, new Set());
        memberships.get(info.id).add(root.jobInfo.id);
      }
      pending.push(...(info.dependencies ?? []));
    }
  }
  return memberships;
}

/**
 * Projects retained JobEventSnapshot occurrences into renderer positions without changing the stream.
 * Replay uses arrival order for channel ownership and publication time for lifecycle precedence.
 * A snapshot is a complete retained window; rebuilding also removes evicted jobs and memberships.
 */
export function projectEventStream(snapshot) {
  if (!snapshot || !Array.isArray(snapshot.queues) || !Array.isArray(snapshot.events))
    throw new TypeError('queues and events are required');
  const queues = new Map();
  const groupIds = new Set();
  for (const queue of snapshot.queues) {
    requiredId(queue.id);
    requiredId(queue.queueId);
    if (queues.has(queue.queueId) || groupIds.has(queue.id)) throw new TypeError('Duplicate queue ID');
    if (!Number.isInteger(queue.maxParallelism) || queue.maxParallelism < 1 || queue.maxParallelism > 1024)
      throw new TypeError('Invalid maxParallelism');
    queues.set(queue.queueId, queue);
    groupIds.add(queue.id);
  }
  const jobs = new Map();
  for (const observation of snapshot.events) {
    const info = observation.jobInfo;
    const queue = queues.get(info?.crossQueueId);
    if (!queue) throw new TypeError('Unknown queue');
    const id = requiredId(info.id);
    const at = observation.timestamp;
    const time = timestamp(at);
    const channel = observation.executionChannel ?? null;
    if (channel !== null && (!Number.isInteger(channel) || channel < 0 || channel >= queue.maxParallelism))
      throw new TypeError('Invalid execution channel');
    if (!Array.isArray(info.dependencies)) throw new TypeError('dependencies must be an array');
    const status = typeof observation.status === 'number' ? statuses[observation.status] : observation.status;
    let job = jobs.get(id);
    if (!job) {
      job = { id, name: info.name, queueId: queue.id, first: at, enqueue: null, start: null, end: null,
        channel: null, channelAt: -Infinity, channelStart: null, status: undefined, statusAt: -Infinity,
        retries: 0, dependencies: new Set() };
      jobs.set(id, job);
    }
    if (job.queueId !== queue.id) throw new TypeError('A job cannot belong to multiple queues');
    job.first = earlier(job.first, at);
    for (const dependency of info.dependencies) job.dependencies.add(requiredId(dependency.id));

    // A terminal identity keeps its captured channel, but a delayed matching Started can enrich it.
    if (channel !== null && !(terminal.has(job.status) && job.channel !== null) && time >= job.channelAt) {
      if (channel !== job.channel) job.channelStart = null;
      job.channel = channel;
      job.channelAt = time;
    }
    if (status === 'Started' && channel !== null && channel === job.channel)
      job.channelStart = earlier(job.channelStart, at);
    if (queued.has(status)) job.enqueue = earlier(job.enqueue, at);
    if (running.has(status)) job.start = earlier(job.start, at);
    if (terminal.has(status)) job.end = later(job.end, at);
    if (rank(status) > rank(job.status) || rank(status) === rank(job.status) && time >= job.statusAt) {
      job.status = status;
      job.statusAt = time;
    }
    job.retries = Math.max(job.retries, observation.retryCount ?? 0);
  }

  const fallbackRoots = rootMembership(snapshot, jobs);
  return {
    queues: snapshot.queues.map(q => ({ id: q.id, name: q.name, maxParallelism: q.maxParallelism })),
    jobs: [...jobs.values()].sort((a, b) => timestamp(a.first) - timestamp(b.first) || a.id.localeCompare(b.id)).map(job => {
      const roots = Object.hasOwn(snapshot.rootJobIds ?? {}, job.id)
        ? snapshot.rootJobIds[job.id] : [...(fallbackRoots.get(job.id) ?? [])];
      if (!Array.isArray(roots)) throw new TypeError('rootJobIds must contain arrays');
      const rootJobIds = [...new Set(roots.map(requiredId))].sort();
      const assigned = job.channel !== null && job.channelStart !== null;
      const metadata = { event: job.status ?? 'Unknown', retryCount: String(job.retries),
        durationSource: 'observer lifecycle timestamps',
        timestampSource: assigned ? 'Started' : job.enqueue !== null ? 'Enqueued' : 'FirstObserved' };
      if (job.enqueue !== null) metadata.enqueuedAt = job.enqueue;
      return { id: job.id, name: job.name, description: '', queueId: job.queueId,
        channel: assigned ? job.channel : null, observedAt: assigned ? job.channelStart : job.enqueue ?? job.first,
        enqueuedAt: job.enqueue, state: state(job.status, job.retries),
        durationMs: job.start !== null && job.end !== null ? Math.max(0, timestamp(job.end) - timestamp(job.start)) : null,
        dependsOn: [...job.dependencies].sort(), metadata, rootJobIds,
        rootJobId: rootJobIds.includes(job.id) ? job.id : rootJobIds.length === 1 ? rootJobIds[0] : null,
        isRoot: rootJobIds.includes(job.id) };
    })
  };
}
