/** Resolve a selected timestamp at the current plot height without moving any event. */
export function inspectionWindow(points, selected, currentWindow, plotHeight, spacing) {
  let nearest = Infinity;
  for (const point of points) {
    if (point.queueId !== selected.queueId || point.channel !== selected.channel) continue;
    const distance = Math.abs(point.time - selected.time);
    if (distance > 0) nearest = Math.min(nearest, distance);
  }
  return Math.max(1, Math.min(currentWindow, nearest * plotHeight / (spacing + 2)));
}

/** Fit the complete inspected group; selection must not crop its other members. */
export function groupWindow(members) {
  const start = members.reduce((min, j) => Math.min(min, j.time), Infinity);
  const end = members.reduce((max, j) => Math.max(max, j.time), -Infinity);
  const windowMs = Math.max(1, (end - start) * 1.1);
  return { windowMs, referenceTime: (start + end) / 2 + windowMs / 2 };
}

/** Expand the time axis enough to separate distinct same-lane timestamps in a complete group. */
export function groupPlotHeight(members, windowMs, minimumHeight, spacing) {
  const lanes = new Map();
  for (const member of members) {
    const key = JSON.stringify([member.queueId, member.channel]);
    if (!lanes.has(key)) lanes.set(key, new Set());
    lanes.get(key).add(member.time);
  }
  let nearest = Infinity;
  for (const times of lanes.values()) {
    const ordered = [...times].sort((a, b) => a - b);
    for (let i = 1; i < ordered.length; i++) nearest = Math.min(nearest, ordered[i] - ordered[i - 1]);
  }
  return Math.max(minimumHeight, Math.ceil(windowMs * (spacing + 2) / nearest));
}

/** Count identities and their current state, even when inspecting retained enqueue positions. */
export function summarize(points) {
  const jobs = [...new Map(points.map(j => [j.id, j])).values()];
  const states = {};
  for (const job of jobs) {
    const state = job.currentState ?? job.state;
    states[state] = (states[state] ?? 0) + 1;
  }
  return { jobCount: jobs.length, state: jobs.length > 0 && states.completed === jobs.length ? 'completed' : 'incomplete',
    rootCount: new Set(jobs.flatMap(j => j.rootJobIds)).size,
    states, stateText: Object.entries(states).map(([state, count]) => `${count} ${state}`).join(' · ') };
}
