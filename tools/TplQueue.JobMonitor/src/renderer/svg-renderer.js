const ns = 'http://www.w3.org/2000/svg';
function element(name, attrs = {}, text) {
  const node = document.createElementNS(ns, name);
  for (const [key, value] of Object.entries(attrs)) node.setAttribute(key, value);
  if (text !== undefined) node.textContent = text;
  return node;
}
const title = (node, text) => node.append(element('title', {}, text));
const symbols = { waiting: 'W', running: '▶', completed: '✓', retried: '↻', failed: '×', cancelled: '−', unknown: '?' };

/** Mechanical SVG adapter. Grouping, geometry and time mapping belong to layout. */
export function render(svg, view, selectedId, focusedIds, ruler, highlightedId = selectedId) {
  svg.setAttribute('width', view.width);
  svg.setAttribute('height', view.height);
  const fragment = document.createDocumentFragment();
  for (const q of view.queues) {
    fragment.append(element('rect', { x: q.x, y: view.headerHeight, width: q.width,
      height: view.height - view.headerHeight, class: 'queue-body' }));
    for (const channel of q.channels)
      fragment.append(element('line', { x1: channel.x, x2: channel.x, y1: view.headerHeight, y2: view.height, class: 'channel-line' }));
    if (q.hasUnassigned) fragment.append(element('rect', { x: q.unassignedLeft, y: view.headerHeight,
      width: q.unassignedWidth, height: view.height - view.headerHeight, class: 'unassigned-body' }));
    if (q.unassignedExpanded) fragment.append(element('line', { x1: q.unassignedX, x2: q.unassignedX,
      y1: view.headerHeight, y2: view.height, class: 'unassigned-line' }));
  }
  for (const tick of view.ticks) fragment.append(element('line', {
    x1: view.gutter, x2: view.width, y1: tick.y, y2: tick.y, class: 'time-grid' }));
  fragment.append(element('line', { x1: view.gutter, x2: view.width, y1: view.referenceY, y2: view.referenceY, class: 'reference-line' }));
  for (const edge of view.edges) fragment.append(element('path', { d: edge.path,
    class: `dependency ${focusedIds && (!focusedIds.has(edge.from) || !focusedIds.has(edge.to)) ? 'dim' : ''}` }));

  const highlight = view.nodes.find(n => n.id === highlightedId && !n.hidden && n.time >= view.startTime && n.time <= view.referenceTime);
  if (highlight) {
    fragment.append(element('line', { x1: view.gutter, x2: view.width, y1: highlight.y, y2: highlight.y, class: 'alignment-guide' }));
    fragment.append(element('line', { x1: highlight.x, x2: highlight.x, y1: view.plotTop, y2: view.referenceY, class: 'alignment-guide' }));
  }
  for (const n of view.markers) {
    if (n.kind === 'cluster') {
      const selected = n.members.some(j => j.id === selectedId);
      const node = element('g', { 'data-cluster-id': n.id, tabindex: '0', role: 'button',
        'aria-label': n.description, 'aria-pressed': String(selected),
        class: `cluster ${selected ? 'selected' : ''} ${focusedIds && !n.members.some(j => focusedIds.has(j.id)) ? 'dim' : ''}` });
      node.append(element('line', { x1: n.x, x2: n.x, y1: n.startY, y2: n.endY, class: 'cluster-range' }));
      for (const y of [n.startY, n.endY]) node.append(element('line', {
        x1: n.x - 5, x2: n.x + 5, y1: y, y2: y, class: 'cluster-range' }));
      node.append(element('rect', { x: n.x - n.size / 2, y: n.y - n.size / 2,
        width: n.size, height: n.size, rx: 5, class: 'cluster-box' }));
      node.append(element('text', { x: n.x, y: n.y, 'text-anchor': 'middle',
        'dominant-baseline': 'central', class: 'cluster-count', textLength: n.label.length > 2 ? n.size - 4 : n.label.length * 8,
        lengthAdjust: 'spacingAndGlyphs' }, n.label));
      title(node, n.description);
      fragment.append(node);
      continue;
    }
    const node = element('g', { 'data-job-id': n.id, tabindex: '0', role: 'button',
      'aria-label': `${n.name}, ${n.state}, ${n.queueName}, ${n.channel === null ? 'unassigned' : `channel ${n.channel}`}, ${n.observedAt}`,
      'aria-pressed': String(n.id === selectedId),
      class: `job ${n.state} ${n.id === selectedId ? 'selected' : ''} ${focusedIds && !focusedIds.has(n.id) ? 'dim' : ''}` });
    node.append(element('rect', { x: n.x - n.targetSize / 2, y: n.y - n.targetSize / 2,
      width: n.targetSize, height: n.targetSize, class: `job-outline ${n.isRoot ? 'root-node' : ''}` }));
    node.append(element('rect', { x: n.x - n.size / 2, y: n.y - n.size / 2,
      width: n.size, height: n.size, class: 'position-marker' }));
    node.append(element('text', { x: n.x + 7, y: n.y - 5, 'text-anchor': 'middle',
      class: 'state-symbol', 'aria-hidden': 'true' }, symbols[n.state] ?? '?'));
    title(node, `${n.name}\nID: ${n.id}\nQueue: ${n.queueName}\nChannel: ${n.channel ?? 'unassigned'}\nState: ${n.state}\nObserved: ${n.observedAt}\nDuration: ${n.durationMs === null ? 'unknown' : `${n.durationMs} ms`}\n${n.description}\n${n.metadataText}`);
    fragment.append(node);
  }
  for (const q of view.queues) {
    fragment.append(element('rect', { x: q.x, y: 0, width: q.width, height: view.headerHeight, class: 'queue-header' }));
    const label = element('text', { x: q.x + 8, y: 20, class: 'queue-label', 'aria-label': q.name }, q.label);
    title(label, `${q.name} — MaxParallelism ${q.maxParallelism}`);
    fragment.append(label);
    for (const channel of q.channels) fragment.append(element('text', { x: channel.x, y: 44,
      'text-anchor': 'middle', class: 'channel-label' }, String(channel.index)));
    if (q.hasUnassigned) {
      const toggle = element('g', { 'data-unassigned-id': q.id, tabindex: '0', role: 'button',
        'aria-expanded': String(q.unassignedExpanded), class: 'unassigned-toggle',
        'aria-label': `${q.unassignedExpanded ? 'Collapse' : 'Expand'} Unassigned in ${q.name}, ${q.unassignedCount} jobs, ${q.unassignedVisibleCount} in this time window` });
      toggle.append(element('rect', { x: q.unassignedLeft, y: 0, width: q.unassignedWidth,
        height: view.headerHeight, class: 'unassigned-header' }));
      toggle.append(element('text', { x: q.unassignedX, y: 20, 'text-anchor': 'middle',
        class: 'channel-label' }, 'U'));
      toggle.append(element('text', { x: q.unassignedX, y: 44, 'text-anchor': 'middle',
        class: 'channel-label' }, `${q.unassignedExpanded ? '‹' : '›'} ${q.unassignedCount}`));
      title(toggle, `Unassigned: ${q.unassignedCount} total; ${q.unassignedVisibleCount} in this time window. Click or press Enter to ${q.unassignedExpanded ? 'collapse' : 'expand'}. Search finds older waiting jobs.`);
      fragment.append(toggle);
    }
    const separator = element('rect', { x: q.x + q.width - 4, y: 0, width: 8, height: view.height,
      class: 'separator', 'data-queue-id': q.id, tabindex: '0', role: 'separator', 'aria-orientation': 'vertical',
      'aria-label': `Resize ${q.name}`, 'aria-valuenow': Math.round(q.width), 'aria-valuemin': Math.round(q.minimumWidth) });
    title(separator, 'Drag or use left/right arrow keys to resize');
    fragment.append(separator);
  }
  const active = svg.querySelector(':focus');
  const focusAttribute = ['data-job-id', 'data-cluster-id', 'data-queue-id', 'data-unassigned-id'].find(a => active?.hasAttribute(a));
  const focusValue = focusAttribute && active.getAttribute(focusAttribute);
  svg.replaceChildren(fragment);
  if (focusAttribute) [...svg.querySelectorAll('[tabindex]')].find(n =>
    n.getAttribute(focusAttribute) === focusValue)?.focus({ preventScroll: true });

  if (ruler) {
    ruler.setAttribute('width', view.gutter); ruler.setAttribute('height', view.height);
    const labels = [element('rect', { width: view.gutter, height: view.height, class: 'ruler-background' }),
      element('text', { x: 8, y: 20, class: 'queue-label' }, 'Time (UTC)')];
    for (const tick of view.ticks) {
      labels.push(element('text', { x: 8, y: tick.y + 4, class: 'time-label' }, tick.label));
      labels.push(element('line', { x1: view.gutter - 8, x2: view.gutter, y1: tick.y, y2: tick.y, class: 'time-tick' }));
    }
    if (highlight) {
      labels.push(element('rect', { x: 0, y: highlight.y - 12, width: view.gutter, height: 24, class: 'ruler-highlight' }));
      labels.push(element('text', { x: 8, y: highlight.y + 4, class: 'exact-time' }, highlight.timeLabel));
      labels.push(element('line', { x1: view.gutter - 8, x2: view.gutter, y1: highlight.y, y2: highlight.y, class: 'alignment-guide' }));
    }
    ruler.replaceChildren(...labels);
  }
}

