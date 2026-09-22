const states = new Set(['waiting', 'running', 'completed', 'retried', 'failed', 'cancelled']);
const text = (value, max = 256) => String(value ?? '').slice(0, max);
const id = value => { if (typeof value !== 'string' || !value.trim()) throw new TypeError('An ID is required'); return value; };
export function timestamp(value) {
  if (typeof value !== 'string' || !/(Z|[+-]\d\d:\d\d)$/i.test(value) || !Number.isFinite(Date.parse(value)))
    throw new TypeError('A timestamp with an explicit timezone is required');
  return Date.parse(value);
}
function metadata(value) {
  if (!value || typeof value !== 'object') return '';
  return Object.entries(value).slice(0, 12).map(([key, item]) => `${text(key, 48)}: ${
    text(typeof item === 'object' ? '[structured value]' : item, 160)}`).join('\n');
}
export function normalize(snapshot) {
  if (!snapshot || !Array.isArray(snapshot.queues) || !Array.isArray(snapshot.jobs)) throw new TypeError('queues and jobs are required');
  const queueIds = new Map();
  const queues = snapshot.queues.map(q => {
    const queue = { id: id(q.id), name: text(q.name || q.id), maxParallelism: q.maxParallelism };
    if (!Number.isInteger(queue.maxParallelism) || queue.maxParallelism < 1 || queue.maxParallelism > 1024)
      throw new TypeError('Invalid maxParallelism');
    if (queueIds.has(queue.id)) throw new TypeError('Duplicate queue ID');
    queueIds.set(queue.id, queue);
    return Object.freeze(queue);
  });
  const jobIds = new Set();
  const jobs = snapshot.jobs.map(j => {
    const queue = queueIds.get(j.queueId);
    if (!queue) throw new TypeError('Unknown queue');
    if (j.channel !== null && (!Number.isInteger(j.channel) || j.channel < 0 || j.channel >= queue.maxParallelism))
      throw new TypeError('Invalid execution channel');
    const jobId = id(j.id);
    if (jobIds.has(jobId)) throw new TypeError('Duplicate job ID');
    jobIds.add(jobId);
    const state = j.state === 'canceled' ? 'cancelled' : j.state === 'queued' ? 'waiting' : j.state;
    if (j.dependsOn !== undefined && !Array.isArray(j.dependsOn)) throw new TypeError('dependsOn must be an array');
    return Object.freeze({ id: jobId, queueId: queue.id, queueName: queue.name, channel: j.channel,
      rootJobId: j.rootJobId == null ? null : id(j.rootJobId), name: text(j.name || jobId),
      description: text(j.description, 512), observedAt: j.observedAt, time: timestamp(j.observedAt),
      enqueuedAt: j.enqueuedAt ?? null, enqueuedTime: j.enqueuedAt == null ? null : timestamp(j.enqueuedAt),
      state: states.has(state) ? state : 'unknown', durationMs: Number.isFinite(j.durationMs) && j.durationMs >= 0 ? j.durationMs : null,
      dependsOn: Object.freeze([...new Set((j.dependsOn ?? []).map(id))].filter(d => d !== jobId)),
      metadataText: metadata(j.metadata), isRoot: j.isRoot === true });
  });
  if (snapshot.referenceTime != null) timestamp(snapshot.referenceTime);
  return Object.freeze({ queues: Object.freeze(queues), jobs: Object.freeze(jobs), referenceTime: snapshot.referenceTime });
}

/** Presentation positions share a job identity; enqueue history is not another job. */
export function positions(model) {
  return model.jobs.flatMap(job => {
    const phase = job.channel === null ? 'enqueue' : 'execution';
    const current = { ...job, phase, markerId: JSON.stringify([job.id, phase]) };
    if (job.channel === null || job.enqueuedTime === null) return [current];
    return [current, { ...job, phase: 'enqueue', markerId: JSON.stringify([job.id, 'enqueue']),
      channel: null, observedAt: job.enqueuedAt, time: job.enqueuedTime, state: 'waiting', durationMs: null }];
  });
}
