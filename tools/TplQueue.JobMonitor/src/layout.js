import { options } from './config.js';

function timeTicks(start, end, pixelsPerMs, plotTop) {
  const desired = Math.max(1, 48 / pixelsPerMs);
  const magnitude = 10 ** Math.floor(Math.log10(desired));
  const step = [1, 2, 5, 10].map(n => n * magnitude).find(n => n >= desired);
  const ticks = [];
  for (let time = Math.ceil(start / step) * step; time <= end; time += step) {
    ticks.push({ time, y: plotTop + (time - start) * pixelsPerMs,
      label: new Date(time).toISOString().slice(11, 23) });
  }
  return ticks;
}

/** Group visible, intersecting hit areas within their real lane; never displace timestamps. */
function groupMarkers(nodes, config, start, end) {
  const lanes = new Map(), markers = [], clusters = [];
  for (const node of nodes) {
    if (node.hidden || node.time < start || node.time > end) continue;
    const key = JSON.stringify([node.queueId, node.channel]);
    if (!lanes.has(key)) lanes.set(key, []);
    lanes.get(key).push(node);
  }
  for (const lane of lanes.values()) {
    lane.sort((a, b) => a.time - b.time || a.id.localeCompare(b.id));
    let members = [];
    const flush = () => {
      if (!members.length) return;
      if (members.length === 1) { markers.push(members[0]); return; }
      const first = members[0], last = members.at(-1);
      const cluster = { kind: 'cluster', id: JSON.stringify(members.map(n => n.id)),
        queueId: first.queueId, queueName: first.queueName, channel: first.channel,
        x: first.x, y: (first.y + last.y) / 2, startY: first.y, endY: last.y,
        startTime: first.time, endTime: last.time, members,
        size: config.clusterSize, label: String(members.length),
        description: `${members.length} jobs, ${first.queueName}, ${first.channel === null ? 'unassigned' : `channel ${first.channel}`}, ${first.observedAt} to ${last.observedAt}. Inspect this interval.` };
      markers.push(cluster); clusters.push(cluster);
    };
    for (const node of lane) {
      if (members.length && node.y - members.at(-1).y >= config.clusterSize + config.markerGap) {
        flush(); members = [];
      }
      members.push(node);
    }
    flush();
  }
  return { markers, clusters };
}

/** Pure geometry. The reference is the end of the window; all job centers retain true time. */
export function layout(model, input = {}) {
  const config = options(input);
  const height = Math.max(240, input.height ?? 600), referenceTime = input.referenceTime ?? 0;
  const plotTop = config.headerHeight + config.clusterSize / 2 + 4;
  const referenceY = height - config.bottomPadding;
  const windowMs = config.windowMs / config.scale, startTime = referenceTime - windowMs;
  const pixelsPerMs = (referenceY - plotTop) / windowMs;
  const channelWidth = Math.max(config.minChannelWidth, config.channelWidth);
  const markerSize = Math.min(config.maxMarkerSize, config.minMarkerSize * config.scale);
  let x = config.gutter;
  const queues = model.queues.map(q => {
    const unassigned = model.jobs.filter(j => j.queueId === q.id && j.channel === null);
    const hasUnassigned = unassigned.length > 0;
    const unassignedExpanded = hasUnassigned && (input.expandedUnassigned?.has(q.id) ?? false);
    const unassignedWidth = hasUnassigned ? (unassignedExpanded ? config.unassignedExpandedWidth : config.unassignedCollapsedWidth) : 0;
    const baseMinimumWidth = q.maxParallelism * channelWidth + config.queuePadding * 2;
    const baseWidth = Math.max(baseMinimumWidth, input.widths?.[q.id] ?? 0);
    const width = baseWidth + unassignedWidth, minimumWidth = baseMinimumWidth + unassignedWidth;
    const slotWidth = (baseWidth - config.queuePadding * 2) / q.maxParallelism;
    const queue = { ...q, x, width, baseWidth, minimumWidth, slotWidth, hasUnassigned,
      unassignedExpanded, unassignedWidth, unassignedLeft: x + baseWidth,
      unassignedCount: unassigned.length,
      unassignedVisibleCount: unassigned.filter(j => j.time >= startTime && j.time <= referenceTime).length,
      label: baseWidth < q.name.length * 8 + 16 ? q.name.slice(0, 1) : q.name,
      channels: Array.from({ length: q.maxParallelism }, (_, i) => ({ index: i,
        x: x + config.queuePadding + (i + .5) * slotWidth })),
      unassignedX: x + baseWidth + unassignedWidth / 2 };
    x += width;
    return queue;
  });
  const byQueue = new Map(queues.map(q => [q.id, q]));
  const nodes = model.jobs.map(job => {
    const queue = byQueue.get(job.queueId);
    const trueY = referenceY + (job.time - referenceTime) * pixelsPerMs;
    return { ...job, kind: 'job', x: job.channel === null ? queue.unassignedX : queue.channels[job.channel].x,
      y: trueY, trueY, timeLabel: new Date(job.time).toISOString().slice(11, 23),
      size: markerSize, targetSize: config.targetSize, unassigned: job.channel === null,
      hidden: job.channel === null && !queue.unassignedExpanded };
  });
  const { markers, clusters } = groupMarkers(nodes, config, startTime, referenceTime);
  const byId = new Map(nodes.map(n => [n.id, n])), representatives = new Map();
  for (const marker of markers)
    for (const member of marker.members ?? [marker]) representatives.set(member.id, marker);
  const edges = [];
  let missingEdges = 0;
  for (const node of nodes) for (const id of node.dependsOn) {
    if (!byId.has(id)) { missingEdges++; continue; }
    const from = representatives.get(id), to = representatives.get(node.id);
    if (!from || !to || from === to) continue;
    const dx = to.x - from.x, dy = to.y - from.y;
    const length = Math.max(Math.abs(dx), Math.abs(dy));
    const a = from.size / 2 / length, b = to.size / 2 / length;
    edges.push({ from: id, to: node.id,
      path: `M ${from.x + dx * a} ${from.y + dy * a} L ${to.x - dx * b} ${to.y - dy * b}` });
  }
  return { queues, nodes, markers, clusters, edges, missingEdges, width: x + 8, height,
    referenceY, referenceTime, plotTop, startTime, windowMs, markerSize,
    ticks: timeTicks(startTime, referenceTime, pixelsPerMs, plotTop),
    headerHeight: config.headerHeight, gutter: config.gutter, pixelsPerMs };
}

