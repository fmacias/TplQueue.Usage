import { options } from './config.js';
import { normalize, timestamp, positions } from './model.js';
import { layout } from './layout.js';
import { search, neighborhood } from './graph.js';
import { inspectionWindow, groupWindow, groupPlotHeight } from './inspection.js';
import { renderInspection } from './inspection-view.js';
import { render } from './renderer/svg-renderer.js';
import { template } from './template.js';

export class JobQueueTimeline extends HTMLElement {
  #model = normalize({ queues: [], jobs: [] });
  #config = options(); #widths = {}; #reference = Date.now(); #live = true;
  #selected = null; #focused = null; #hovered = null;
  #selectedPhase = null;
  #resize; #events; #frame; #view; #drag;
  #historyMin = 0; #historyMax = 1; #historyTarget = 0;
  #overview = null; #inspectionIds = null;
  #pendingModel = null;
  #inspectionRoot = null;
  #inspectionQueue = null;
  #resolveGroup = false;
  #groupMagnification = 1;
  #expandedUnassigned = new Set();
  constructor() {
    super();
    this.attachShadow({ mode: 'open' });
    this.shadowRoot.innerHTML = `<link rel="stylesheet" href="${new URL('./styles.css', import.meta.url).href}">${template}`;
  }
  get selectedJobId() { return this.#selected; }
  get referenceTime() { return this.#reference; }
  get isFollowingLive() { return this.#live; }
  get visibleWindowMs() { return this.#config.windowMs / this.#config.scale; }
  connectedCallback() {
    if (this.#events) return;
    this.#events = new AbortController();
    const listen = (target, event, action, settings = {}) => target.addEventListener(event, action,
      { ...settings, signal: this.#events.signal });
    listen(this.shadowRoot, 'click', e => this.#click(e));
    listen(this.shadowRoot.querySelector('input[type=search]'), 'input', () => this.#search());
    listen(this.shadowRoot.querySelector('.reference-input'), 'change', e => {
      if (e.target.value) this.setReferenceTime(`${e.target.value}Z`);
    });
    listen(this.shadowRoot, 'keydown', e => this.#key(e));
    listen(this.shadowRoot, 'focusin', e => {
      const job = this.#view?.nodes.find(j => j.markerId === e.target.getAttribute?.('data-marker-id'));
      if (job) {
        this.shadowRoot.querySelector('.accessible-info').textContent = `${job.name}, ${job.phase}, ${job.state}, ${job.observedAt}, ${job.description}. ${job.relationshipText}. ${job.metadataText}`;
        this.#highlight(job.markerId);
      }
    });
    const svg = this.shadowRoot.querySelector('.timeline');
    listen(svg, 'pointerover', e => this.#highlight(e.target.closest?.('[data-marker-id]')?.getAttribute('data-marker-id') ?? null));
    listen(svg, 'pointerleave', () => this.#highlight(null));
    listen(this.shadowRoot.querySelector('.viewport'), 'wheel', e => {
      if (Math.abs(e.deltaX) > Math.abs(e.deltaY) || e.shiftKey || e.ctrlKey) return;
      if (e.currentTarget.classList.contains('expanded-time')) return;
      e.preventDefault();
      if (this.#view) this.setReferenceTime(this.#reference + e.deltaY / this.#view.pixelsPerMs);
    }, { passive: false });
    listen(this.shadowRoot.querySelector('.viewport'), 'scroll', () => this.#alignRuler());
    listen(this.shadowRoot.querySelector('.history'), 'scroll', e => {
      if (Math.abs(e.target.scrollTop - this.#historyTarget) < 2) return;
      const fraction = e.target.scrollTop / Math.max(1, e.target.scrollHeight - e.target.clientHeight);
      this.setReferenceTime(this.#historyMin + fraction * (this.#historyMax - this.#historyMin));
    });
    listen(this.shadowRoot, 'pointerdown', e => {
      const id = e.target.getAttribute?.('data-queue-id');
      if (!id) return;
      const queue = this.#view.queues.find(q => q.id === id);
      this.#drag = { id, x: e.clientX, width: queue.baseWidth };
      e.preventDefault();
    });
    listen(window, 'pointermove', e => {
      if (!this.#drag) return;
      this.#widths[this.#drag.id] = this.#drag.width + e.clientX - this.#drag.x;
      this.#schedule();
    });
    listen(window, 'pointerup', () => { this.#drag = null; });
    listen(window, 'pointercancel', () => { this.#drag = null; });
    this.#resize = new ResizeObserver(() => {
      const job = this.#inspectionIds?.length === 1 && positions(this.#model).find(j => j.id === this.#selected && j.phase === this.#selectedPhase);
      if (job && job.time >= this.#reference - this.visibleWindowMs && job.time <= this.#reference) {
        const center = this.#reference - this.visibleWindowMs / 2;
        const windowMs = this.#selectionWindow(job);
        if (windowMs < this.visibleWindowMs) {
          this.#config = options({ ...this.#config, scale: this.#config.windowMs / windowMs });
          this.#reference = center + this.visibleWindowMs / 2;
        }
      }
      this.#schedule();
    });
    this.#resize.observe(this);
    this.#resize.observe(this.shadowRoot.querySelector('.viewport'));
    this.#schedule();
  }
  disconnectedCallback() {
    this.#events?.abort(); this.#events = null;
    this.#resize?.disconnect(); clearTimeout(this.#frame);
    this.#frame = null; this.#drag = null; this.#hovered = null;
  }
  setData(snapshot) {
    const model = normalize(snapshot);
    if (this.#inspectionIds) {
      this.#pendingModel = model;
      this.shadowRoot.querySelector('[data-action=refresh-inspection]').hidden = false;
      return;
    }
    this.#acceptModel(model);
  }
  #acceptModel(model) {
    this.#model = model;
    for (const id of this.#expandedUnassigned)
      if (!model.queues.some(q => q.id === id)) this.#expandedUnassigned.delete(id);
    if (model.referenceTime != null) this.setReferenceTime(model.referenceTime);
    // Incoming snapshots drive live time. Idle monitors never poll or redraw on a clock.
    else if (this.#live) {
      const next = Date.now() - this.#config.liveLagMs;
      // Hold the current interval before a visible group would be scrolled away.
      // State still refreshes; only Follow live explicitly catches up again.
      if (this.#view?.clusters.some(group => group.startTime < next - this.visibleWindowMs &&
          group.members.some(member => model.jobs.some(job => job.id === member.id)))) this.#live = false;
      else this.#reference = next;
    }
    if (this.#selected && !model.jobs.some(j => j.id === this.#selected)) this.clearFocus();
    else if (this.#selected) this.#focused = neighborhood(model, this.#selected);
    this.#renderInspection(); this.#search(); this.#schedule();
  }
  configure(input) { this.#config = options({ ...this.#config, ...input }); this.#schedule(); }
  setReferenceTime(value) {
    const reference = typeof value === 'string' ? timestamp(value) : value;
    if (!Number.isFinite(reference) || Math.abs(reference) > 8.64e15) throw new TypeError('Invalid reference time');
    this.#reference = reference; this.#live = false; this.#schedule();
  }
  followLive() {
    this.#overview = null; this.#inspectionIds = null;
    this.#inspectionRoot = null;
    this.#inspectionQueue = null;
    this.#applyPending();
    this.#config = options({ ...this.#config, windowMs: 5000, scale: 1 });
    this.#live = true; this.#reference = Date.now() - this.#config.liveLagMs;
    this.#renderInspection(); this.#schedule();
  }
  showOverview() {
    const saved = this.#overview;
    this.#overview = null; this.#inspectionIds = null;
    this.#inspectionRoot = null;
    this.#inspectionQueue = null;
    this.#applyPending();
    this.#config = options({ ...this.#config, windowMs: 5000, scale: 1 });
    if (saved) { this.#reference = saved.reference; this.#live = saved.live; }
    if (this.#live) this.#reference = Date.now() - this.#config.liveLagMs;
    this.#renderInspection(); this.#schedule();
  }
  clearFocus() { this.#selected = null; this.#selectedPhase = null; this.#focused = null; this.#hovered = null; this.#renderInspection(); this.#schedule(); }
  focusJob(id, phase) {
    const points = positions(this.#model).filter(j => j.id === id);
    const job = points.find(j => j.phase === phase) ?? points[0];
    if (!job) return false;
    this.#rememberOverview();
    if (!this.#inspectionIds?.includes(job.markerId)) {
      this.#inspectionIds = [job.markerId]; this.#inspectionRoot = null; this.#inspectionQueue = null;
      this.#resolveGroup = false;
    }
    if (job.channel === null || job.enqueuedTime !== null) this.#expandedUnassigned.add(job.queueId);
    this.#selectedPhase = job.phase;
    this.#selected = id; this.#hovered = null; this.#focused = neighborhood(this.#model, id);
    for (const marker of this.#inspectionIds) this.#focused.add(JSON.parse(marker)[0]);
    this.#renderInspection();
    if (this.#inspectionIds.length > 1) this.#fitInspection();
    else {
      const windowMs = this.#selectionWindow(job);
      this.configure({ scale: this.#config.windowMs / windowMs });
      this.setReferenceTime(job.time + this.visibleWindowMs / 2);
    }
    this.#draw(); this.#renderInspection();
    const viewport = this.shadowRoot.querySelector('.viewport');
    const node = this.#view.nodes.find(n => n.markerId === job.markerId);
    viewport.scrollLeft = Math.max(0, node.x - (viewport.clientWidth + this.#view.gutter) / 2);
    if (viewport.classList.contains('expanded-time')) {
      viewport.scrollTop = Math.max(0, node.y - viewport.clientHeight / 2);
      this.#alignRuler();
    }
    this.dispatchEvent(new CustomEvent('job-select', { bubbles: true, composed: true,
      detail: { jobId: id, rootJobId: job.rootJobId, rootJobIds: job.rootJobIds } }));
    return true;
  }
  #selectionWindow(job) {
    const height = Math.max(240, this.shadowRoot.querySelector('.viewport').clientHeight - 18);
    const plotHeight = height - this.#config.bottomPadding - this.#config.headerHeight - this.#config.clusterSize / 2 - 4;
    return inspectionWindow(positions(this.#model), job, this.visibleWindowMs, plotHeight,
      this.#config.clusterSize + this.#config.markerGap);
  }
  #highlight(id) {
    if (this.#hovered === id) return;
    this.#hovered = id; this.#schedule();
  }
  #rememberOverview() {
    if (!this.#overview) this.#overview = { reference: this.#reference, live: this.#live };
    this.#live = false;
  }
  #zoom(factor) {
    if (factor > 1 && !this.#inspectionIds && this.#view?.clusters.length) {
      this.#inspectGroup(this.#view.markers.flatMap(marker => marker.members ?? [marker]));
      return;
    }
    if (this.#resolveGroup && this.#inspectionIds?.length > 1) {
      this.#groupMagnification = Math.max(1, Math.min(100, this.#groupMagnification * factor));
      this.#schedule(); return;
    }
    this.#rememberOverview();
    const center = this.#reference - this.visibleWindowMs / 2;
    this.configure({ scale: this.#config.scale * factor });
    this.#reference = center + this.visibleWindowMs / 2;
  }
  #inspectCluster(id) {
    const cluster = this.#view?.clusters.find(n => n.id === id);
    if (!cluster) return;
    this.#inspectGroup(cluster.members);
  }
  #inspectGroup(members) {
    this.#rememberOverview();
    this.clearFocus();
    this.#inspectionRoot = null;
    this.#inspectionQueue = null;
    this.#resolveGroup = true;
    this.#groupMagnification = 1;
    this.#inspectionIds = members.map(n => n.markerId);
    for (const member of members) if (member.channel === null) this.#expandedUnassigned.add(member.queueId);
    this.shadowRoot.querySelector('.viewport').scrollTop = 0;
    this.#fitInspection();
    this.#renderInspection();
    this.#draw();
    const first = this.#view.nodes.find(node => node.markerId === members[0].markerId);
    const viewport = this.shadowRoot.querySelector('.viewport');
    viewport.scrollLeft = Math.max(0, first.x - (viewport.clientWidth + this.#view.gutter) / 2);
    viewport.scrollTop = Math.max(0, first.y - this.#view.plotTop);
    this.#alignRuler();
    this.shadowRoot.querySelector('.inspection-heading').focus();
  }
  #fitInspection() {
    const ids = new Set(this.#inspectionIds);
    const members = positions(this.#model).filter(j => ids.has(j.markerId));
    if (!members.length) return;
    const range = groupWindow(members);
    const windowMs = Math.max(5000, range.windowMs);
    this.configure({ windowMs, scale: windowMs / range.windowMs });
    this.setReferenceTime(range.referenceTime);
  }
  #renderInspection() {
    const panel = this.shadowRoot.querySelector('.inspection');
    const ids = new Set(this.#inspectionIds);
    const members = this.#inspectionIds && positions(this.#model).filter(j => ids.has(j.markerId))
      .sort((a, b) => a.time - b.time || a.id.localeCompare(b.id));
    renderInspection(panel, this.#model, members ?? [], this.#selected, this.#pendingModel !== null, this.#inspectionRoot,
      this.#model.queues.find(q => q.id === this.#inspectionQueue)?.name);
    if (!members?.length) this.#inspectionIds = null;
  }
  #applyPending() {
    if (!this.#pendingModel) return;
    const model = this.#pendingModel; this.#pendingModel = null;
    this.#acceptModel(model);
  }
  #refreshInspection() {
    const markers = this.#inspectionIds ?? [];
    const reference = this.#reference;
    this.#inspectionIds = null; this.#applyPending();
    this.#reference = reference; this.#live = false;
    const points = positions(this.#model);
    const byMarker = new Map(points.map(j => [j.markerId, j])), byId = new Map();
    for (const point of points) if (!byId.has(point.id)) byId.set(point.id, point);
    this.#inspectionIds = [...new Set(markers.map(id => byMarker.get(id) ?? byId.get(JSON.parse(id)[0]))
      .filter(Boolean).map(j => j.markerId))];
    if (this.#inspectionRoot) this.#inspectRoot(this.#inspectionRoot);
    else if (this.#inspectionQueue) this.#inspectQueue(this.#inspectionQueue, false);
    if (this.#selected) this.focusJob(this.#selected, this.#selectedPhase);
    else this.#fitInspection();
    this.#renderInspection(); this.#schedule();
  }
  #inspectRoot(id) {
    const members = this.#model.jobs.filter(j => j.id === id || j.rootJobIds.includes(id))
      .map(j => positions({ jobs: [j] })[0]);
    if (!members.length) return;
    this.#rememberOverview();
    this.#inspectionRoot = id;
    this.#inspectionQueue = null;
    this.#resolveGroup = false;
    this.#inspectionIds = members.map(j => j.markerId);
    const selected = members.find(j => j.id === this.#selected);
    if (selected) this.#selectedPhase = selected.phase;
    for (const member of members) if (member.channel === null) this.#expandedUnassigned.add(member.queueId);
    this.#fitInspection();
    this.#renderInspection();
    this.shadowRoot.querySelector('.inspection-heading').focus();
  }
  #inspectQueue(id, clearSelection = true) {
    const members = this.#model.jobs.filter(j => j.queueId === id).map(j => positions({ jobs: [j] })[0]);
    if (!members.length) return;
    this.#rememberOverview();
    if (clearSelection) this.clearFocus();
    this.#inspectionRoot = null; this.#inspectionQueue = id;
    this.#resolveGroup = false;
    this.#inspectionIds = members.map(j => j.markerId);
    const selected = members.find(j => j.id === this.#selected);
    if (selected) this.#selectedPhase = selected.phase;
    if (members.some(j => j.channel === null)) this.#expandedUnassigned.add(id);
    this.#fitInspection(); this.#renderInspection(); this.#draw();
    const queue = this.#view.queues.find(q => q.id === id);
    this.shadowRoot.querySelector('.viewport').scrollLeft = Math.max(0, queue.x - this.#view.gutter);
    this.shadowRoot.querySelector('.inspection-heading').focus();
  }
  #renderQueueSummary() {
    const summary = this.shadowRoot.querySelector('.queue-summary');
    const active = summary.querySelector(':focus')?.dataset.inspectQueue;
    summary.replaceChildren(...this.#model.queues.map(queue => {
      const jobs = this.#model.jobs.filter(j => j.queueId === queue.id);
      const activeCount = jobs.filter(j => ['waiting', 'running', 'retried'].includes(j.state)).length;
      const outside = jobs.filter(j => j.time < this.#reference - this.visibleWindowMs || j.time > this.#reference).length;
      const button = document.createElement('button'); button.type = 'button'; button.dataset.inspectQueue = queue.id;
      button.disabled = jobs.length === 0;
      button.textContent = `${queue.name} · ${jobs.length} jobs · ${activeCount} active · ${outside} outside interval`;
      button.title = `Inspect all observed jobs in ${queue.name}, including history outside the visible time interval.`;
      return button;
    }));
    if (active) [...summary.children].find(b => b.dataset.inspectQueue === active)?.focus({ preventScroll: true });
  }
  #search() {
    const results = this.shadowRoot.querySelector('.results');
    const query = this.shadowRoot.querySelector('input[type=search]').value;
    results.replaceChildren(...search(this.#model, query, this.#config.maxSearchResults).map(job => {
      const button = document.createElement('button'); button.type = 'button'; button.dataset.result = job.id;
      button.textContent = `${job.name} · ${job.id}`; return button;
    }));
  }
  #click(e) {
    const target = e.target.closest?.('[data-action],[data-job-id],[data-result],[data-cluster-id],[data-unassigned-id],[data-root-id],[data-inspect-queue]');
    if (!target) return;
    if (target.hasAttribute('data-inspect-queue')) { this.#inspectQueue(target.dataset.inspectQueue); return; }
    if (target.hasAttribute('data-root-id')) { this.#inspectRoot(target.dataset.rootId); return; }
    if (target.hasAttribute('data-unassigned-id')) { this.#toggleUnassigned(target.getAttribute('data-unassigned-id')); return; }
    if (target.hasAttribute('data-cluster-id')) { this.#inspectCluster(target.getAttribute('data-cluster-id')); return; }
    const id = target.getAttribute('data-job-id') ?? target.getAttribute('data-result');
    if (id) {
      const fromNavigation = target.closest('.inspection-relations,.results');
      this.focusJob(id, target.dataset.phase);
      if (fromNavigation) this.shadowRoot.querySelector('.inspection-heading').focus();
      return;
    }
    switch (target.dataset.action) {
      case 'clear': this.clearFocus(); break;
      case 'live': this.followLive(); break;
      case 'pause': this.#live = false; this.#schedule(); break;
      case 'overview': this.showOverview(); this.shadowRoot.querySelector('.viewport').focus(); break;
      case 'close-inspection':
        this.#inspectionRoot = null;
        this.#inspectionQueue = null;
        this.#inspectionIds = null; this.#applyPending(); this.#renderInspection(); this.#schedule(); this.shadowRoot.querySelector('.viewport').focus(); break;
      case 'refresh-inspection': this.#refreshInspection(); break;
      case 'in': this.#zoom(2); break;
      case 'out': this.#zoom(.5); break;
    }
  }
  #toggleUnassigned(id) {
    if (this.#expandedUnassigned.has(id)) this.#expandedUnassigned.delete(id);
    else this.#expandedUnassigned.add(id);
    this.#hovered = null; this.#schedule();
  }
  #key(e) {
    const queueId = e.target.getAttribute?.('data-queue-id');
    if (queueId && ['ArrowLeft', 'ArrowRight'].includes(e.key)) {
      const queue = this.#view.queues.find(q => q.id === queueId);
      this.#widths[queueId] = queue.baseWidth + (e.key === 'ArrowRight' ? 24 : -24); this.#schedule(); e.preventDefault();
    }
    if (['Enter', ' '].includes(e.key)) {
      if (e.target.hasAttribute?.('data-unassigned-id')) { this.#toggleUnassigned(e.target.getAttribute('data-unassigned-id')); e.preventDefault(); }
      else if (e.target.hasAttribute?.('data-job-id')) { this.focusJob(e.target.getAttribute('data-job-id'), e.target.dataset.phase); e.preventDefault(); }
      else if (e.target.hasAttribute?.('data-cluster-id')) { this.#inspectCluster(e.target.getAttribute('data-cluster-id')); e.preventDefault(); }
    }
    if (e.key === 'Escape' && (this.#overview || this.#inspectionIds)) {
      this.showOverview(); this.shadowRoot.querySelector('.viewport').focus(); e.preventDefault();
    }
    if (e.target.classList?.contains('viewport') && ['ArrowUp','ArrowDown','PageUp','PageDown'].includes(e.key)) {
      if (e.target.classList.contains('expanded-time')) return;
      this.setReferenceTime(this.#reference + (e.key.endsWith('Up') ? -1 : 1) *
        this.visibleWindowMs * (e.key.startsWith('Page') ? 1 : .1));
      e.preventDefault();
    }
  }
  #schedule() {
    if (!this.isConnected || this.#frame != null) return;
    this.#frame = setTimeout(() => { this.#frame = null; this.#draw(); }, 16);
  }
  #draw() {
    this.#renderQueueSummary();
    const viewport = this.shadowRoot.querySelector('.viewport');
    let height = Math.max(240, viewport.clientHeight - 18);
    const resolveGroup = this.#resolveGroup && this.#inspectionIds?.length > 1;
    if (resolveGroup) {
      const points = positions(this.#model), ids = new Set(this.#inspectionIds);
      const range = groupWindow(points.filter(point => ids.has(point.markerId)));
      const members = points.filter(point => point.time >= range.referenceTime - range.windowMs &&
        point.time <= range.referenceTime && (point.channel !== null || this.#expandedUnassigned.has(point.queueId)));
      const padding = this.#config.bottomPadding + this.#config.headerHeight + this.#config.clusterSize / 2 + 4;
      height = padding + this.#groupMagnification * groupPlotHeight(members, range.windowMs, height - padding,
        this.#config.clusterSize + this.#config.markerGap);
    }
    viewport.classList.toggle('expanded-time', Boolean(resolveGroup));
    if (!resolveGroup) viewport.scrollTop = 0;
    this.#view = layout(this.#model, { ...this.#config, referenceTime: this.#reference,
      height, widths: this.#widths, expandedUnassigned: this.#expandedUnassigned });
    const selected = this.#view.nodes.find(n => n.id === this.#selected && n.phase === this.#selectedPhase)
      ?? this.#view.nodes.find(n => n.id === this.#selected);
    render(this.shadowRoot.querySelector('.timeline'), this.#view, this.#selected, this.#focused,
      this.shadowRoot.querySelector('.time-ruler'), this.#hovered ?? selected?.markerId);
    this.#alignRuler();
    this.shadowRoot.querySelector('.mode').textContent = `${this.#live ? 'LIVE' : 'HISTORY'} · ${this.#model.jobs.length} jobs${this.#view.missingEdges ? ` · ${this.#view.missingEdges} unresolved dependencies` : ''}`;
    const duration = this.visibleWindowMs;
    this.shadowRoot.querySelector('.scale-value').textContent = duration >= 1000 ? `${+(duration / 1000).toFixed(2)} s` : `${+duration.toFixed(2)} ms`;
    this.shadowRoot.querySelector('[data-action=overview]').hidden = !this.#overview && duration === 5000;
    this.shadowRoot.querySelector('[data-action=out]').disabled = this.#config.scale === 1;
    this.shadowRoot.querySelector('[data-action=in]').disabled = duration <= 1;
    this.shadowRoot.querySelector('[data-action=pause]').disabled = !this.#live;
    const input = this.shadowRoot.querySelector('.reference-input');
    if (this.shadowRoot.activeElement !== input) input.value = new Date(this.#reference).toISOString().slice(0,23);
    this.#historyMin = this.#view.nodes.reduce((min, j) => Math.min(min, j.time), this.#reference) - 60000;
    this.#historyMax = this.#view.nodes.reduce((max, j) => Math.max(max, j.time), this.#reference) + 60000;
    const history = this.shadowRoot.querySelector('.history');
    this.#historyTarget = (this.#reference - this.#historyMin) / (this.#historyMax - this.#historyMin) * (history.scrollHeight - history.clientHeight);
    history.scrollTop = this.#historyTarget;
  }
  #alignRuler() {
    this.shadowRoot.querySelector('.time-ruler').style.transform =
      `translateY(${-this.shadowRoot.querySelector('.viewport').scrollTop}px)`;
  }
}
if (!customElements.get('job-queue-timeline')) customElements.define('job-queue-timeline', JobQueueTimeline);

