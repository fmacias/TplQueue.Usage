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
