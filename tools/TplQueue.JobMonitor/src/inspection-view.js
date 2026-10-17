import { summarize } from './inspection.js';

const shortId = id => id.length > 16 ? `${id.slice(0, 8)}…${id.slice(-4)}` : id;
const element = (tag, text, className) => {
  const node = document.createElement(tag); node.textContent = text;
  if (className) node.className = className;
  return node;
};
function jobButton(job, selected) {
  const button = element('button', `${job.isRoot ? 'R · ' : ''}${job.name} · ${shortId(job.id)} · ${job.currentState ?? job.state}`);
  button.type = 'button'; button.dataset.result = job.id;
  button.title = `ID: ${job.id}\nRoot IDs: ${job.rootJobIds.join(', ') || 'not supplied'}`;
  button.setAttribute('aria-label', `${button.textContent}. ${button.title}`);
  button.setAttribute('aria-pressed', String(job.id === selected));
  return button;
}

/** Render the captured interval and explicit dependency navigation, independently of time geometry. */
export function renderInspection(panel, model, members, selectedId, hasUpdates, rootId, queueName) {
  panel.hidden = !members.length;
  if (panel.hidden) return;
  const summary = summarize(members);
  const memberIds = new Set(members.map(j => j.id));
  const internal = model.jobs.filter(j => memberIds.has(j.id))
    .reduce((count, j) => count + j.dependsOn.filter(id => memberIds.has(id)).length, 0);
  panel.querySelector('.inspection-heading').textContent = `${queueName ? `${queueName} · ` : rootId ? 'Root group · ' : ''}${summary.jobCount} ${summary.jobCount === 1 ? 'job' : 'jobs'} in inspection`;
  panel.querySelector('.inspection-note').textContent =
    `${new Date(members[0].time).toISOString()} — ${new Date(members.at(-1).time).toISOString()}. ` +
    `${summary.rootCount} root memberships · ${summary.stateText} · ${internal} internal dependencies. ` +
    `${rootId ? `Composed root: ${rootId}. ` : ''}Captured snapshot. The full group remains on the timeline; scroll vertically when expanded. Use the list for individual selection, including identical timestamps.`;
  panel.querySelector('[data-action=refresh-inspection]').hidden = !hasUpdates;
  const list = panel.querySelector('.inspection-jobs');
  const activeId = list.querySelector(':focus')?.dataset.markerId;
  list.replaceChildren(...members.map(job => {
    const button = jobButton(job, selectedId);
    button.dataset.phase = job.phase; button.dataset.markerId = job.markerId;
    button.textContent = `${new Date(job.time).toISOString().slice(11, 23)} UTC · ${button.textContent} · ${job.phase}` +
      ` · roots: ${job.rootJobIds.map(shortId).join(', ') || 'not supplied'}`;
    button.setAttribute('aria-label', `${job.observedAt}, ${job.phase}. ${button.getAttribute('aria-label')}`);
    return button;
  }));
  if (activeId) [...list.children].find(b => b.dataset.markerId === activeId)?.focus({ preventScroll: true });
  const relations = panel.querySelector('.inspection-relations');
  const selected = model.jobs.find(j => j.id === selectedId);
  relations.replaceChildren();
  if (!selected) return;
  const byId = new Map(model.jobs.map(j => [j.id, j]));
  relations.append(element('h3', 'Prerequisites → selected job → dependents'));
  const appendRelations = (ids, empty, direction) => {
    if (!ids.length) relations.append(element('span', empty));
    for (const id of ids) {
      const job = byId.get(id);
      if (!job) { relations.append(element('span', `Unresolved prerequisite: ${id}`)); continue; }
      const button = jobButton(job, selectedId);
      button.textContent = `${direction} ${button.textContent}`;
      relations.append(button);
    }
  };
  appendRelations(selected.dependsOn, 'No prerequisites', '↓ From');
  relations.append(element('div', `${selected.isRoot ? 'Root' : 'Job'}: ${selected.name}\nID: ${selected.id}\n` +
    `State: ${selected.state}\nRoot IDs: ${selected.rootJobIds.join(', ') || 'not supplied'}`, 'selected-identity'));
  appendRelations(model.jobs.filter(j => j.dependsOn.includes(selected.id)).map(j => j.id), 'No dependents', '↓ To');
  for (const id of new Set([...selected.rootJobIds, ...(selected.isRoot ? [selected.id] : [])])) {
    const button = element('button', `Inspect root group ${shortId(id)}`);
    button.type = 'button'; button.dataset.rootId = id; button.title = id;
    relations.append(button);
  }
}
