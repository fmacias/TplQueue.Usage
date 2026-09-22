import { options } from './config.js';
import { normalize, timestamp, positions } from './model.js';
import { layout } from './layout.js';
import { search, connected } from './graph.js';
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
        this.shadowRoot.querySelector('.accessible-info').textContent = `${job.name}, ${job.phase}, ${job.state}, ${job.observedAt}, ${job.description}. ${job.metadataText}`;
        this.#highlight(job.markerId);
      }
    });
    const svg = this.shadowRoot.querySelector('.timeline');
    listen(svg, 'pointerover', e => this.#highlight(e.target.closest?.('[data-marker-id]')?.getAttribute('data-marker-id') ?? null));
    listen(svg, 'pointerleave', () => this.#highlight(null));
    listen(this.shadowRoot.querySelector('.viewport'), 'wheel', e => {
      if (Math.abs(e.deltaX) > Math.abs(e.deltaY) || e.shiftKey || e.ctrlKey) return;
      e.preventDefault();
      if (this.#view) this.setReferenceTime(this.#reference + e.deltaY / this.#view.pixelsPerMs);
    }, { passive: false });
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
    this.#resize = new ResizeObserver(() => this.#schedule());
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
    this.#model = model;
    for (const id of this.#expandedUnassigned)
      if (!model.queues.some(q => q.id === id)) this.#expandedUnassigned.delete(id);
    if (model.referenceTime != null) this.setReferenceTime(model.referenceTime);
    // Incoming snapshots drive live time. Idle monitors never poll or redraw on a clock.
    else if (this.#live) this.#reference = Date.now() - this.#config.liveLagMs;
    if (this.#selected && !model.jobs.some(j => j.id === this.#selected)) this.clearFocus();
    else if (this.#selected) this.#focused = connected(model, this.#selected);
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
    this.#config = options({ ...this.#config, windowMs: 5000, scale: 1 });
    this.#live = true; this.#reference = Date.now() - this.#config.liveLagMs;
    this.#renderInspection(); this.#schedule();
  }
  showOverview() {
    const saved = this.#overview;
    this.#overview = null; this.#inspectionIds = null;
    this.#config = options({ ...this.#config, windowMs: 5000, scale: 1 });
    if (saved) { this.#reference = saved.reference; this.#live = saved.live; }
    if (this.#live) this.#reference = Date.now() - this.#config.liveLagMs;
    this.#renderInspection(); this.#schedule();
  }
  clearFocus() { this.#selected = null; this.#selectedPhase = null; this.#focused = null; this.#hovered = null; this.#schedule(); }
  focusJob(id, phase) {
    const points = positions(this.#model).filter(j => j.id === id);
    const job = points.find(j => j.phase === phase) ?? points[0];
    if (!job) return false;
    if (job.channel === null || job.enqueuedTime !== null) this.#expandedUnassigned.add(job.queueId);
    this.#selectedPhase = job.phase;
    this.#selected = id; this.#hovered = null; this.#focused = connected(this.#model, id);
    this.setReferenceTime(job.time + this.visibleWindowMs / 2);
    this.#draw(); this.#renderInspection();
    const viewport = this.shadowRoot.querySelector('.viewport');
    const node = this.#view.nodes.find(n => n.markerId === job.markerId);
    viewport.scrollLeft = Math.max(0, node.x - (viewport.clientWidth + this.#view.gutter) / 2);
    this.dispatchEvent(new CustomEvent('job-select', { bubbles: true, composed: true, detail: { jobId: id, rootJobId: job.rootJobId } }));
    return true;
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
    this.#rememberOverview();
    const center = this.#reference - this.visibleWindowMs / 2;
    this.configure({ scale: this.#config.scale * factor });
    this.#reference = center + this.visibleWindowMs / 2;
  }
  #inspectCluster(id) {
    const cluster = this.#view?.clusters.find(n => n.id === id);
    if (!cluster) return;
    this.#rememberOverview();
    this.#inspectionIds = cluster.members.map(n => n.markerId);
    const span = cluster.endTime - cluster.startTime;
    const windowMs = Math.min(this.visibleWindowMs, Math.max(1, span * 1.6));
    this.configure({ scale: this.#config.windowMs / windowMs });
    this.setReferenceTime((cluster.startTime + cluster.endTime) / 2 + this.visibleWindowMs / 2);
    this.#renderInspection();
    this.shadowRoot.querySelector('.inspection-heading').focus();
  }
  #renderInspection() {
    const panel = this.shadowRoot.querySelector('.inspection');
    const members = this.#inspectionIds && positions(this.#model).filter(j => this.#inspectionIds.includes(j.markerId))
      .sort((a, b) => a.time - b.time || a.id.localeCompare(b.id));
    panel.hidden = !members?.length;
    if (panel.hidden) { this.#inspectionIds = null; return; }
    this.shadowRoot.querySelector('.inspection-heading').textContent = `${members.length} jobs in this interval`;
    this.shadowRoot.querySelector('.inspection-note').textContent =
      'Each entry retains its exact timestamp. Jobs at the same time share a count marker.';
    const list = this.shadowRoot.querySelector('.inspection-jobs');
    const activeId = list.querySelector(':focus')?.dataset.markerId;
    list.replaceChildren(...members.map(job => {
      const button = document.createElement('button'); button.type = 'button'; button.dataset.result = job.id;
      button.dataset.phase = job.phase; button.dataset.markerId = job.markerId;
      button.setAttribute('aria-pressed', String(job.id === this.#selected));
      button.textContent = `${new Date(job.time).toISOString().slice(11, 23)} UTC · ${job.name} · ${job.phase} · ${job.state}`;
      return button;
    }));
    if (activeId) [...list.children].find(b => b.dataset.markerId === activeId)?.focus({ preventScroll: true });
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
    const target = e.target.closest?.('[data-action],[data-job-id],[data-result],[data-cluster-id],[data-unassigned-id]');
    if (!target) return;
    if (target.hasAttribute('data-unassigned-id')) { this.#toggleUnassigned(target.getAttribute('data-unassigned-id')); return; }
    if (target.hasAttribute('data-cluster-id')) { this.#inspectCluster(target.getAttribute('data-cluster-id')); return; }
    const id = target.getAttribute('data-job-id') ?? target.getAttribute('data-result');
    if (id) { this.focusJob(id, target.dataset.phase); return; }
    switch (target.dataset.action) {
      case 'clear': this.clearFocus(); break;
      case 'live': this.followLive(); break;
      case 'pause': this.#live = false; this.#schedule(); break;
      case 'overview': this.showOverview(); this.shadowRoot.querySelector('.viewport').focus(); break;
      case 'close-inspection':
        this.#inspectionIds = null; this.#renderInspection(); this.shadowRoot.querySelector('.viewport').focus(); break;
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
    const viewport = this.shadowRoot.querySelector('.viewport');
    this.#view = layout(this.#model, { ...this.#config, referenceTime: this.#reference,
      height: Math.max(240, viewport.clientHeight - 18), widths: this.#widths, expandedUnassigned: this.#expandedUnassigned });
    const selected = this.#view.nodes.find(n => n.id === this.#selected && n.phase === this.#selectedPhase)
      ?? this.#view.nodes.find(n => n.id === this.#selected);
    render(this.shadowRoot.querySelector('.timeline'), this.#view, this.#selected, this.#focused,
      this.shadowRoot.querySelector('.time-ruler'), this.#hovered ?? selected?.markerId);
    this.shadowRoot.querySelector('.mode').textContent = `${this.#live ? 'LIVE' : 'HISTORY'} · ${this.#model.jobs.length} jobs${this.#view.missingEdges ? ` · ${this.#view.missingEdges} unresolved dependencies` : ''}`;
    const duration = this.visibleWindowMs;
    this.shadowRoot.querySelector('.scale-value').textContent = duration >= 1000 ? `${+(duration / 1000).toFixed(2)} s` : `${+duration.toFixed(2)} ms`;
    this.shadowRoot.querySelector('[data-action=overview]').hidden = !this.#overview && duration === 5000;
    this.shadowRoot.querySelector('[data-action=out]').disabled = this.#config.scale === 1;
    this.shadowRoot.querySelector('[data-action=in]').disabled = duration <= 1;
    this.shadowRoot.querySelector('[data-action=pause]').disabled = !this.#live;
    const input = this.shadowRoot.querySelector('.reference-input');
    if (this.shadowRoot.activeElement !== input) input.value = new Date(this.#reference).toISOString().slice(0,23);
    const times = this.#view.nodes.map(j => j.time);
    this.#historyMin = Math.min(this.#reference, ...times) - 60000;
    this.#historyMax = Math.max(this.#reference, ...times) + 60000;
    const history = this.shadowRoot.querySelector('.history');
    this.#historyTarget = (this.#reference - this.#historyMin) / (this.#historyMax - this.#historyMin) * (history.scrollHeight - history.clientHeight);
    history.scrollTop = this.#historyTarget;
  }
}
if (!customElements.get('job-queue-timeline')) customElements.define('job-queue-timeline', JobQueueTimeline);

