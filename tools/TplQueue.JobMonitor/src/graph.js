export function search(model, query, limit = 30) {
  const term = query.trim().toLocaleLowerCase();
  if (!term) return [];
  return model.jobs.filter(j => [j.id, j.name, j.description].some(value => value.toLocaleLowerCase().includes(term))).slice(0, limit);
}
export function connected(model, jobId) {
  const neighbors = new Map(model.jobs.map(j => [j.id, new Set()]));
  for (const job of model.jobs) for (const dependency of job.dependsOn) {
    if (!neighbors.has(dependency)) continue;
    neighbors.get(job.id).add(dependency);
    neighbors.get(dependency).add(job.id);
  }
  const found = new Set(), pending = neighbors.has(jobId) ? [jobId] : [];
  while (pending.length) {
    const current = pending.pop();
    if (found.has(current)) continue;
    found.add(current);
    pending.push(...neighbors.get(current));
  }
  return found;
}

/** Composed run memberships plus one dependency hop; FIFO history must not swallow focus. */
export function neighborhood(model, jobId) {
  const selected = model.jobs.find(j => j.id === jobId);
  if (!selected) return new Set();
  const roots = new Set(selected.rootJobIds);
  if (selected.isRoot) roots.add(selected.id);
  const members = new Set(model.jobs.filter(j => j.id === jobId || roots.has(j.id) ||
    j.rootJobIds.some(root => roots.has(root))).map(j => j.id));
  const result = new Set(members);
  for (const job of model.jobs) for (const prerequisite of job.dependsOn) {
    if (members.has(job.id)) result.add(prerequisite);
    if (members.has(prerequisite)) result.add(job.id);
  }
  return result;
}
