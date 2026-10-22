import '../src/job-queue-timeline.js';
import { sampleData, reference } from '../demo/data.js';
import { attach, update, detach } from '../integrations/blazor/job-monitor.js';
const viewer = document.querySelector('job-queue-timeline'), results = document.querySelector('#results');
const frame = () => new Promise(resolve => setTimeout(resolve, 100));
const assert = (value, message) => { if (!value) throw new Error(message); };
const representedIds = () => {
  const root=viewer.shadowRoot;
  const ids=new Set([...root.querySelectorAll('.timeline .job')].map(n=>n.dataset.jobId));
  for(const group of root.querySelectorAll('.timeline .cluster'))
    for(const marker of JSON.parse(group.dataset.clusterId)) ids.add(JSON.parse(marker)[0]);
  return ids;
};
let passed = 0, failed = 0;
async function check(name, action) {
  const li = document.createElement('li'); results.append(li);
  try { await action(); passed++; li.className='pass'; li.textContent=`PASS ${name}`; }
  catch(error) { failed++; li.className='fail'; li.textContent=`FAIL ${name}: ${error.message}`; }
}
viewer.setData(sampleData()); viewer.setReferenceTime(reference); await frame();
await check('live view stays idle until a new snapshot arrives', async () => {
  viewer.followLive(); await frame();
  const before=viewer.referenceTime;
  let mutations=0;
  const observer=new MutationObserver(records=>mutations+=records.length);
  observer.observe(viewer.shadowRoot.querySelector('.timeline'),{childList:true,subtree:true,attributes:true});
  try {
    await new Promise(resolve=>setTimeout(resolve,650));
    assert(viewer.referenceTime===before,'idle reference must not follow a timer');
    assert(mutations===0,'idle SVG must not redraw');
    const snapshot=sampleData(); delete snapshot.referenceTime;
    viewer.setData(snapshot); await frame();
    assert(viewer.referenceTime>before && viewer.isFollowingLive,'snapshot advances live reference');
    assert(mutations>0,'snapshot redraws the view');
  } finally { observer.disconnect(); viewer.setReferenceTime(reference); await frame(); }
});
await check('snapshots update paused state without moving its reference', async () => {
  viewer.setReferenceTime(reference); await frame();
  const snapshot=sampleData(); delete snapshot.referenceTime;
  snapshot.jobs.push({...snapshot.jobs[0],id:'paused-arrival'});
  viewer.setData(snapshot); await frame();
  assert(viewer.referenceTime===Date.parse(reference) && !viewer.isFollowingLive,'history remains fixed');
  assert(viewer.shadowRoot.querySelector('.mode').textContent.includes(`${snapshot.jobs.length} jobs`),'new data appears while paused');
  viewer.setData(sampleData()); await frame();
});
await check('reconnecting a live monitor does not restart periodic refresh', async () => {
  viewer.followLive(); await frame();
  const before=viewer.referenceTime;
  try {
    viewer.remove(); document.body.append(viewer); await frame();
    await new Promise(resolve=>setTimeout(resolve,650));
    assert(viewer.referenceTime===before && viewer.isFollowingLive,'reconnection retains an idle live reference');
  } finally { viewer.setReferenceTime(reference); await frame(); }
});
await check('dark default and all logical channels', () => {
  assert(getComputedStyle(viewer).getPropertyValue('--jm-bg').trim()==='#1e1e1e','dark theme');
  assert(viewer.shadowRoot.querySelectorAll('.channel-line').length===21,'channel count');
  assert(viewer.shadowRoot.querySelectorAll('.unassigned-toggle').length===1,'explicit unassigned area');
  assert(!viewer.shadowRoot.querySelector('.unassigned-line'),'unassigned collapsed initially');
});
await check('horizontal overflow retains dense channels', () => {
  const viewport=viewer.shadowRoot.querySelector('.viewport'); assert(viewport.scrollWidth>viewport.clientWidth,'horizontal overflow');
});
await check('cross-queue dependencies and safe metadata tooltip', () => {
  assert(viewer.shadowRoot.querySelectorAll('.dependency').length>=7,'dependencies');
  assert([...viewer.shadowRoot.querySelectorAll('.job title')].some(n=>n.textContent.includes('sample.measurement.v1')),'metadata');
});
await check('search focuses graph and emits selection once', async () => {
  let count=0; const listener=()=>count++; viewer.addEventListener('job-select',listener);
  const input=viewer.shadowRoot.querySelector('input[type=search]'); input.value='normalize'; input.dispatchEvent(new Event('input',{bubbles:true}));
  viewer.shadowRoot.querySelector('.results button').click(); await frame();
  assert(viewer.selectedJobId==='normalize-units' && count===1,'selection');
  assert(viewer.shadowRoot.querySelector('.dim'),'unrelated graph dimmed'); viewer.removeEventListener('job-select',listener);
});
await check('selection survives snapshot refresh', async () => {
  viewer.setData(sampleData()); await frame(); assert(viewer.selectedJobId==='normalize-units','selection survives');
});
await check('invalid snapshot is atomic and null is accepted', async () => {
  const before=viewer.shadowRoot.querySelectorAll('.job').length, invalid=sampleData(); invalid.jobs[0].channel=99;
  let rejected=false; try { viewer.setData(invalid); } catch { rejected=true; } await frame();
  assert(rejected && viewer.shadowRoot.querySelectorAll('.job').length===before,'atomic validation');
});
await check('zoom preserves readable node size', async () => {
  viewer.configure({scale:.01}); await frame();
  const markers = [...viewer.shadowRoot.querySelectorAll('.position-marker')];
  assert(markers.length > 0 && markers.every(n=>+n.getAttribute('width')>=2),'minimum square size');
  assert([...viewer.shadowRoot.querySelectorAll('.job-outline')].every(n=>+n.getAttribute('width')>=24),'readable outline and hit area');
  assert(!viewer.shadowRoot.querySelector('.job circle'),'square markers replace circles');
});
await check('keyboard queue resize', async () => {
  const separator=viewer.shadowRoot.querySelector('[data-queue-id="fifo"]'); const before=+separator.getAttribute('aria-valuenow');
  separator.dispatchEvent(new KeyboardEvent('keydown',{key:'ArrowRight',bubbles:true})); await frame();
  assert(+viewer.shadowRoot.querySelector('[data-queue-id="fifo"]').getAttribute('aria-valuenow')>before,'resize');
});
await check('reference navigation and live follow', async () => {
  viewer.setReferenceTime(reference); await frame(); const before=viewer.referenceTime;
  const viewport=viewer.shadowRoot.querySelector('.viewport'); viewport.dispatchEvent(new KeyboardEvent('keydown',{key:'PageDown',bubbles:true}));
  await frame(); assert(viewer.referenceTime>before&&!viewer.isFollowingLive,'history');
  viewer.followLive(); assert(viewer.isFollowingLive,'live'); viewer.setReferenceTime(reference);
});
await check('history scrollbar input moves reference', async () => {
  await frame(); const before=viewer.referenceTime, history=viewer.shadowRoot.querySelector('.history');
  assert(history.scrollHeight>history.clientHeight,'native scrollable history');
  history.scrollTop+=100; history.dispatchEvent(new Event('scroll')); await frame(); assert(viewer.referenceTime!==before,'scroll reference');
});
await check('adapter replaces subscriptions and detaches safely', async () => {
  let calls=0; const callback={invokeMethodAsync:async()=>{calls++;}};
  const observations = {
    queues: [{ id: 'parallel', queueId: 'runtime-queue', name: 'ParallelQ', maxParallelism: 3 }],
    events: [{ status: 4, timestamp: reference, executionChannel: 1, retryCount: 0,
      jobInfo: { id: 'root-summary', name: 'Summary', crossQueueId: 'runtime-queue', dependencies: [] } }],
    rootJobIds: { 'root-summary': ['root-summary'] }
  };
  attach(viewer,callback); attach(viewer,callback); update(viewer,JSON.stringify(observations)); viewer.focusJob('root-summary');
  assert(calls===1,'no duplicate callbacks'); detach(viewer); viewer.focusJob('root-summary'); assert(calls===1,'detached');
  update(viewer,{ ...observations, events: [] });
  assert(viewer.selectedJobId==='root-summary','detached updates are ignored');
  viewer.setData(sampleData()); viewer.setReferenceTime(reference);
});
await check('disconnect and reconnect retain one active listener', async () => {
  viewer.remove(); document.body.append(viewer); await frame();
  let calls=0; const listener=()=>calls++; viewer.addEventListener('job-select',listener);
  viewer.focusJob('normalize-units'); await frame(); const node=viewer.shadowRoot.querySelector('[data-job-id="normalize-units"]'); node.dispatchEvent(new MouseEvent('click',{bubbles:true}));
  assert(calls===2,'single event per selection'); viewer.removeEventListener('job-select',listener);
});
await check('no permanent job detail panel', () => assert(!viewer.shadowRoot.querySelector('aside,dialog,.details-panel'),'no details panel'));
await check('five seconds, compact channels, straight connectors and visible time ruler', async () => {
  viewer.showOverview(); viewer.setData(sampleData()); viewer.setReferenceTime(reference); viewer.clearFocus(); await frame();
  assert(viewer.visibleWindowMs===5000,'fixed overview');
  assert(+viewer.shadowRoot.querySelector('[data-queue-id="parallel"]').getAttribute('aria-valuenow')===136,'compact three-channel queue');
  assert(viewer.shadowRoot.querySelectorAll('.time-ruler .time-label').length>=3,'time ruler');
  assert(+viewer.shadowRoot.querySelector('.timeline').getAttribute('height')===Math.max(240,viewer.shadowRoot.querySelector('.viewport').clientHeight-18),'window fills viewport after stylesheet load');
  assert([...viewer.shadowRoot.querySelectorAll('.dependency')].every(p=>!/[CQ]/.test(p.getAttribute('d'))),'straight paths');
});
await check('close timestamps expand and returning restores the exact overview', async () => {
  viewer.setData(sampleData({denseTiming:true})); viewer.setReferenceTime(reference); await frame();
  const before=viewer.referenceTime;
  const cluster=[...viewer.shadowRoot.querySelectorAll('[data-cluster-id]')].find(n=>n.getAttribute('data-cluster-id').includes('dense-follow-up'));
  assert(cluster,'near-time count marker');
  cluster.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); await frame();
  assert(viewer.visibleWindowMs<5000,'shorter inspection interval');
  assert(!viewer.shadowRoot.querySelector('.inspection').hidden,'temporary job list');
  assert(viewer.shadowRoot.querySelector('[data-job-id="dense-follow-up"]'),'close timestamps separate');
  assert([...viewer.shadowRoot.querySelectorAll('.position-marker')].every(n=>+n.getAttribute('width')===12),'bounded marker growth');
  viewer.shadowRoot.querySelector('[data-action="overview"]').click(); await frame();
  assert(viewer.visibleWindowMs===5000 && viewer.referenceTime===before,'overview restored');
  assert(viewer.shadowRoot.querySelector('.inspection').hidden,'list dismissed');
});
await check('identical timestamps remain individually selectable without fabricated positions', async () => {
  const cluster=[...viewer.shadowRoot.querySelectorAll('[data-cluster-id]')].find(n=>n.getAttribute('data-cluster-id').includes('same-time-example'));
  cluster.dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  assert(viewer.visibleWindowMs===1,'minimum interval');
  assert(viewer.shadowRoot.querySelector('.cluster'),'identical times remain grouped');
  const entries=[...viewer.shadowRoot.querySelectorAll('.inspection-jobs button')];
  assert(entries.length===2,'both jobs listed');
  let count=0; const listener=()=>count++; viewer.addEventListener('job-select',listener);
  entries.find(n=>n.dataset.result==='same-time-example').click(); await frame();
  assert(viewer.selectedJobId==='same-time-example' && count===1,'individual selection');
  assert(viewer.shadowRoot.querySelector('.exact-time').textContent==='11:59:58.000','exact selected timestamp');
  viewer.removeEventListener('job-select',listener);
  viewer.shadowRoot.querySelector('.viewport').dispatchEvent(new KeyboardEvent('keydown',{key:'Escape',bubbles:true})); await frame();
  assert(viewer.visibleWindowMs===5000 && viewer.shadowRoot.querySelector('.inspection').hidden,'escape returns to overview');
});
await check('focused square center aligns with the ruler and channel after resize', async () => {
  viewer.focusJob('normalize-units'); await frame();
  const marker=viewer.shadowRoot.querySelector('[data-job-id="normalize-units"] .position-marker');
  const center=+marker.getAttribute('y') + +marker.getAttribute('height')/2;
  assert(+viewer.shadowRoot.querySelector('.time-ruler .alignment-guide').getAttribute('y1')===center,'time alignment');
  const beforeX=+marker.getAttribute('x') + +marker.getAttribute('width')/2;
  viewer.style.height='640px'; await frame();
  const resized=viewer.shadowRoot.querySelector('[data-job-id="normalize-units"] .position-marker');
  assert(viewer.visibleWindowMs<=5000,'resize preserves a readable inspection interval');
  assert(+resized.getAttribute('x') + +resized.getAttribute('width')/2===beforeX,'channel position stable');
  const viewport=viewer.shadowRoot.querySelector('.viewport'), ruler=viewer.shadowRoot.querySelector('.time-ruler');
  const left=ruler.getBoundingClientRect().left; viewport.scrollLeft=200; await frame();
  assert(ruler.getBoundingClientRect().left===left,'ruler remains visible during horizontal scrolling');
});
await check('pause and live controls preserve the five-second overview', async () => {
  viewer.followLive(); await frame();
  viewer.shadowRoot.querySelector('[data-action="pause"]').click(); const before=viewer.referenceTime; await frame();
  assert(!viewer.isFollowingLive && viewer.referenceTime===before,'pause freezes window');
  viewer.shadowRoot.querySelector('[data-action="in"]').click(); await frame();
  assert(viewer.visibleWindowMs===2500 && !viewer.isFollowingLive,'manual inspection');
  viewer.followLive(); await frame();
  assert(viewer.visibleWindowMs===5000 && viewer.isFollowingLive,'live resets overview');
  viewer.setData(sampleData()); viewer.setReferenceTime(reference);
});
await check('unassigned strip expands by keyboard without moving execution channels', async () => {
  viewer.clearFocus(); await frame();
  const getToggle=()=>viewer.shadowRoot.querySelector('[data-unassigned-id="cache"]');
  if(getToggle().getAttribute('aria-expanded')==='true') {
    getToggle().dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  }
  const before=[...viewer.shadowRoot.querySelectorAll('.channel-line')].map(n=>n.getAttribute('x1'));
  getToggle().focus(); getToggle().dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); await frame();
  assert(getToggle().getAttribute('aria-expanded')==='true','expanded');
  assert(getToggle().querySelector('.channel-label').textContent==='U','compact label when expanded');
  assert(getToggle().querySelector('title').textContent.startsWith('Unassigned:'),'full hover tooltip');
  assert(getToggle().getAttribute('aria-label').includes('Unassigned'),'accessible full name');
  assert(viewer.shadowRoot.querySelector('[data-job-id="waiting-batch"]'),'waiting job revealed');
  // The queue being expanded retains its assigned centers; subsequent queues move right.
  assert([...viewer.shadowRoot.querySelectorAll('.channel-line')].slice(0,6).every((n,i)=>n.getAttribute('x1')===before[i]),'assigned centers stable');
  viewer.setData(sampleData()); await frame();
  assert(getToggle().getAttribute('aria-expanded')==='true','expansion survives snapshots');
  getToggle().dispatchEvent(new KeyboardEvent('keydown',{key:' ',bubbles:true})); await frame();
  assert(!viewer.shadowRoot.querySelector('[data-job-id="waiting-batch"]'),'collapse hides only unassigned markers');
  assert(getToggle().querySelector('.channel-label').textContent==='U','compact label when collapsed');
  assert(viewer.shadowRoot.activeElement===getToggle(),'keyboard focus retained');
});
await check('search reveals enqueue history and Started retains both positions with an assignment arrow', async () => {
  viewer.focusJob('waiting-batch'); await frame();
  assert(viewer.shadowRoot.querySelector('[data-unassigned-id="cache"]').getAttribute('aria-expanded')==='true','search auto-expands');
  const snapshot=sampleData(), job=snapshot.jobs.find(j=>j.id==='waiting-batch');
  const enqueueTime=job.observedAt;
  job.enqueuedAt=enqueueTime;
  job.channel=0; job.observedAt='2026-09-17T11:59:59.800Z'; job.state='running';
  job.metadata={...job.metadata,enqueuedAt:enqueueTime,timestampSource:'Started'};
  viewer.setData(snapshot); await frame();
  viewer.shadowRoot.querySelector('[data-action="refresh-inspection"]').click(); await frame();
  const nodes=viewer.shadowRoot.querySelectorAll('[data-job-id="waiting-batch"]');
  assert(nodes.length===2 && viewer.selectedJobId==='waiting-batch','two positions, same job selection');
  assert(viewer.shadowRoot.querySelector('[data-unassigned-id="cache"]'),'enqueue strip retained');
  const assigned=[...nodes].find(n=>n.dataset.phase==='execution');
  assert(assigned.querySelector('title').textContent.includes('Channel: 0'),'actual channel');
  assert(viewer.shadowRoot.querySelector('.assignment[marker-end]'),'directed enqueue-to-start relation');
  viewer.focusJob('waiting-batch'); await frame();
  assert(viewer.shadowRoot.querySelector('.exact-time').textContent==='11:59:59.800','Started coordinate');
  assert(assigned.querySelector('title').textContent.includes(enqueueTime),'enqueue retained as metadata');
  let selections=0; const listener=e=>{assert(e.detail.jobId==='waiting-batch','original job ID');selections++;};
  viewer.addEventListener('job-select',listener);
  const enqueue=viewer.shadowRoot.querySelector('[data-job-id="waiting-batch"][data-phase="enqueue"]');
  enqueue.focus(); enqueue.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); await frame();
  assert(selections===1 && viewer.selectedJobId==='waiting-batch','enqueue selects original job once');
  assert(viewer.shadowRoot.querySelector('.exact-time').textContent==='11:59:59.000','enqueue coordinate');
  assert(viewer.shadowRoot.activeElement?.dataset.phase==='enqueue','enqueue keyboard focus survives redraw');
  viewer.setData(snapshot); await frame();
  assert(viewer.shadowRoot.querySelectorAll('[data-job-id="waiting-batch"]').length===2,'refresh retains both positions');
  viewer.removeEventListener('job-select',listener);
});

await check('grouped enqueue history is individually selectable at its own time', async () => {
  const snapshot={queues:[{id:'history',name:'History',maxParallelism:2}],jobs:[0,1].map(i=>({
    id:`history-${i}`,queueId:'history',channel:i,name:`History ${i}`,state:'completed',dependsOn:[],
    observedAt:'2026-09-17T11:59:59.000Z',enqueuedAt:'2026-09-17T11:59:57.000Z'}))};
  viewer.setData(snapshot); viewer.showOverview(); viewer.focusJob('history-0'); await frame();
  const cluster=viewer.shadowRoot.querySelector('[data-cluster-id]');
  assert(cluster,'enqueue group');
  cluster.dispatchEvent(new KeyboardEvent('keydown',{key:'Enter',bubbles:true})); await frame();
  const entries=[...viewer.shadowRoot.querySelectorAll('.inspection-jobs button')];
  assert(entries.length===2 && entries.every(n=>n.dataset.phase==='enqueue'),'enqueue inspection entries');
  entries[0].click(); await frame();
  assert(viewer.selectedJobId==='history-0','logical job ID');
  assert(viewer.shadowRoot.querySelector('.exact-time').textContent==='11:59:57.000','inspect enqueue time, not execution');
  assert(!viewer.shadowRoot.querySelector('.assignment'),'out-of-window endpoint is not fabricated');
});
await check('running failed and cancelled shared graphs preserve selection and every root membership', async () => {
  let selected;
  const listener=e=>selected=e.detail;
  viewer.addEventListener('job-select',listener);
  try {
    for (const state of ['running','failed','cancelled']) {
      const make=(id,channel,roots,dependsOn=[])=>({id,queueId:'q',channel,name:id,state,
        rootJobIds:roots,rootJobId:roots.length===1?roots[0]:null,isRoot:roots.includes(id),dependsOn,
        observedAt:'2026-09-17T11:59:59.000Z'});
      viewer.setData({queues:[{id:'q',name:'Queue',maxParallelism:3}],jobs:[
        make('shared',0,['first','second']),make('first',1,['first'],['shared']),
        make('second',2,['second'],['shared']),
        {...make('unrelated',null,[]),observedAt:'2026-09-17T11:59:58.000Z'}]});
      viewer.showOverview(); viewer.focusJob('unrelated'); viewer.focusJob('shared'); await frame();
      assert(selected.jobId==='shared' && selected.rootJobId===null,'shared job has no arbitrary owning root');
      assert(selected.rootJobIds.join(',')==='first,second','all memberships in selection');
      for(const id of ['shared','first','second']) {
        const node=viewer.shadowRoot.querySelector(`[data-job-id="${id}"]`);
        assert(node && !node.classList.contains('dim'),`${state}: connected graph emphasized`);
      }
      assert(viewer.shadowRoot.querySelector('[data-job-id="unrelated"]').classList.contains('dim'),'unrelated job dimmed');
      viewer.focusJob('first'); await frame();
      assert(selected.rootJobIds.join(',')==='first','root selection preserves its identity');
    }
  } finally { viewer.removeEventListener('job-select',listener); }
});

await check('edge labels and keyboard job details distinguish standalone ingest from ETL', async () => {
  const make=(id,channel,roots,dependsOn=[])=>({id,queueId:'cache',channel,name:id,state:'completed',
    rootJobIds:roots,dependsOn,observedAt:'2026-09-17T11:59:59.000Z'});
  viewer.setData({queues:[{id:'cache',name:'CacheQ',maxParallelism:3}],jobs:[
    make('etl-ingest',0,['etl-root']),make('etl-root',1,['etl-root'],['etl-ingest']),
    {...make('single',2,['single']),isRoot:true,enqueuedAt:'2026-09-17T11:59:57.000Z'}]});
  viewer.showOverview(); viewer.setReferenceTime(reference); viewer.focusJob('single'); await frame();
  const assignment=viewer.shadowRoot.querySelector('path.assignment');
  const dependency=viewer.shadowRoot.querySelector('path.dependency');
  assert(assignment?.querySelector('title').textContent.includes('same job single (ID: single)'),
    'assignment identifies the same job');
  assert(dependency?.querySelector('title').textContent.includes('etl-root (ID: etl-root) depends on etl-ingest (ID: etl-ingest)'),
    'dependency identifies both endpoints and direction');
  assert(dependency.getAttribute('aria-label')===dependency.querySelector('title').textContent,
    'edge has an accessible description');
  assert(dependency.classList.contains('dim') && !assignment.classList.contains('dim'),
    'standalone selection does not highlight the ETL dependency');
  const single=viewer.shadowRoot.querySelector('[data-job-id="single"][data-phase="execution"]');
  assert(single.querySelector('title').textContent.includes('Dependency IDs: none'),
    'standalone tooltip explicitly reports no dependencies');
  single.focus(); await frame();
  const details=viewer.shadowRoot.querySelector('.accessible-info').textContent;
  assert(details.includes('Dependency IDs: none') && details.includes('Root IDs: single'),
    'keyboard focus announces dependency and root IDs');
  assert(viewer.shadowRoot.querySelector('footer').textContent.includes('Dashed arrow = same job'),
    'visible legend explains assignment arrows');
});

await check('dense member selection resolves milliseconds and docks without covering queues', async () => {
  viewer.showOverview();
  const base=Date.parse(reference)-1000;
  const snapshot={queues:[{id:'dense',name:'Dense',maxParallelism:1}],jobs:Array.from({length:15},(_,i)=>({
    id:`dense-${i}`,name:'Ingest payload',queueId:'dense',channel:0,state:'completed',
    observedAt:new Date(base+i).toISOString(),isRoot:true,rootJobIds:[`dense-${i}`],dependsOn:i?[`dense-${i-1}`]:[]}))};
  viewer.setData(snapshot); viewer.setReferenceTime(reference); await frame();
  viewer.shadowRoot.querySelector('.cluster').dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  const group=viewer.shadowRoot.querySelector('.inspection-jobs');
  assert(group.children.length===15,'all identities listed');
  group.querySelector('[data-result="dense-7"]').click(); await frame();
  assert(Array.from({length:15},(_,i)=>`dense-${i}`).every(id=>representedIds().has(id)),
    'selection retains every member as a job or count square');
  const panel=viewer.shadowRoot.querySelector('.inspection').getBoundingClientRect();
  const viewport=viewer.shadowRoot.querySelector('.viewport-row').getBoundingClientRect();
  assert(panel.left>=viewport.right || panel.top>=viewport.bottom,'inspector reserves space');
  assert(!viewer.shadowRoot.querySelector('.cluster-range'),'group has no vertical range line');
  assert(viewer.shadowRoot.querySelector('.inspection-relations [data-result="dense-6"]'),'previous root navigation');
  assert(viewer.shadowRoot.querySelector('.inspection-relations [data-result="dense-8"]'),'next root navigation');
  viewer.style.height='440px'; await frame();
  assert(Array.from({length:15},(_,i)=>`dense-${i}`).every(id=>representedIds().has(id)),
    'resizing keeps the whole group visible');
  viewer.style.height='640px'; await frame();
  const before=group.textContent, window=viewer.visibleWindowMs, referenceBefore=viewer.referenceTime;
  const next=structuredClone(snapshot); next.jobs[7].state='failed';
  next.jobs[7].observedAt=new Date(base+1000).toISOString(); viewer.setData(next); await frame();
  assert(group.textContent===before && viewer.referenceTime===referenceBefore && viewer.visibleWindowMs===window,'captured inspection stays stable');
  const refresh=viewer.shadowRoot.querySelector('[data-action="refresh-inspection"]');
  assert(!refresh.hidden,'updates announced'); refresh.click(); await frame();
  assert(viewer.selectedJobId==='dense-7' && group.textContent.includes('failed'),'refresh preserves ID and applies current state');
  viewer.style.width='700px'; await frame();
  const smallPanel=viewer.shadowRoot.querySelector('.inspection').getBoundingClientRect();
  assert(smallPanel.top>=viewer.shadowRoot.querySelector('.viewport-row').getBoundingClientRect().bottom,'narrow inspector below timeline');
  viewer.style.width='950px'; viewer.showOverview(); await frame();
});
await check('group dependencies summarize real pairs and same-time roots remain navigable', async () => {
  const make=(id,channel,dependsOn=[],roots=[id])=>({id,name:'Ingest payload',queueId:'groups',channel,
    state:'completed',observedAt:'2026-09-17T11:59:59.000Z',isRoot:true,rootJobIds:roots,dependsOn});
  viewer.setData({queues:[{id:'groups',name:'Groups',maxParallelism:2}],jobs:[
    make('a',0),make('b',0,['a']),make('c',1,['a','b']),make('d',1,['b'],['c'])]});
  viewer.setReferenceTime(reference); await frame();
  assert(viewer.shadowRoot.querySelectorAll('path.dependency').length===1,'one connector for three relationships');
  assert(viewer.shadowRoot.querySelector('.edge-count').textContent==='×3','counted relationships');
  assert(!viewer.shadowRoot.querySelector('.internal-count'),'no small legend below the count');
  assert(viewer.shadowRoot.querySelector('.cluster title').textContent.includes('1 internal dependencies'),'internal dependencies remain in the tooltip');
  viewer.focusJob('b'); await frame();
  assert(viewer.shadowRoot.querySelector('.cluster.selected'),'same timestamp remains grouped');
  viewer.shadowRoot.querySelector('.inspection-relations [data-result="a"]').click(); await frame();
  assert(viewer.selectedJobId==='a','navigate actual prerequisite');
  viewer.focusJob('c'); await frame();
  viewer.shadowRoot.querySelector('[data-root-id="c"]').click(); await frame();
  assert(viewer.shadowRoot.querySelectorAll('.inspection-jobs button').length===2,'inspect actual composed group across jobs');
  assert(viewer.shadowRoot.querySelector('.inspection-note').textContent.includes('2 completed'),'current states summarized');
  viewer.followLive(); await frame();
});

await check('refreshing a composed group includes newly observed members and handles retention', async () => {
  const root={id:'root',queueId:'q',channel:0,state:'running',isRoot:true,rootJobIds:['root'],observedAt:reference};
  const snapshot={queues:[{id:'q',maxParallelism:2}],jobs:[root]};
  viewer.setData(snapshot); viewer.focusJob('root'); await frame();
  viewer.shadowRoot.querySelector('[data-root-id="root"]').click(); await frame();
  const next=structuredClone(snapshot);
  next.jobs.push({...root,id:'child',channel:1,isRoot:false});
  viewer.setData(next); await frame();
  assert(viewer.shadowRoot.querySelectorAll('.inspection-jobs button').length===1,'captured membership remains stable');
  viewer.shadowRoot.querySelector('[data-action="refresh-inspection"]').click(); await frame();
  assert(viewer.shadowRoot.querySelectorAll('.inspection-jobs button').length===2,'refresh includes newly observed composed member');
  assert(viewer.selectedJobId==='root','root selection retained');
  viewer.setData({queues:snapshot.queues,jobs:[]}); await frame();
  assert(viewer.selectedJobId==='root','retention does not change captured selection');
  viewer.shadowRoot.querySelector('[data-action="refresh-inspection"]').click(); await frame();
  assert(viewer.selectedJobId===null && viewer.shadowRoot.querySelector('.inspection').hidden,'removed selection clears safely on refresh');
  viewer.followLive();
});

await check('CacheQ group inspection keeps its complete time span after selecting either endpoint', async () => {
  viewer.showOverview(); viewer.style.height='640px';
  const base=Date.parse(reference)-1000;
  const jobs=[0,1,40].map((offset,i)=>({id:`cache-${i}`,name:`Cache job ${i}`,queueId:'cache',channel:0,
    state:'completed',observedAt:new Date(base+offset).toISOString(),dependsOn:i?[`cache-${i-1}`]:[]}));
  viewer.setData({queues:[{id:'cache',name:'CacheQ',maxParallelism:1}],jobs});
  viewer.setReferenceTime(reference); await frame();
  assert(representedIds().size===3,'all jobs represented before inspection');
  assert(!viewer.shadowRoot.querySelector('.cluster-range'),'only a count square and dependency edges');
  viewer.shadowRoot.querySelector('.cluster').dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  assert(viewer.visibleWindowMs<5000,'inspection zooms time');
  for(const id of ['cache-0','cache-2']) {
    viewer.shadowRoot.querySelector(`.inspection-jobs [data-result="${id}"]`).click(); await frame();
    assert(jobs.every(j=>representedIds().has(j.id)),'no member disappears on selection');
    assert(!viewer.shadowRoot.querySelector('.timeline .job.dim,.timeline .cluster.dim'),'inspected siblings remain readable');
  }
  viewer.style.height='420px'; await frame();
  assert(jobs.every(j=>representedIds().has(j.id)),'no member disappears on resize');
  viewer.showOverview(); viewer.style.height='640px';
});

await check('queue history remains accessible when all CacheQ jobs leave the live interval', async () => {
  viewer.followLive();
  const base=Date.now()-60000;
  const snapshot={queues:[{id:'cache',name:'CacheQ',maxParallelism:2}],jobs:[0,1,2].map(i=>({
    id:`older-${i}`,queueId:'cache',channel:i===2?null:0,state:i===2?'waiting':'completed',
    observedAt:new Date(base+i*10000).toISOString(),dependsOn:i?[`older-${i-1}`]:[]}))};
  viewer.setData(snapshot); await frame();
  const queue=viewer.shadowRoot.querySelector('[data-inspect-queue="cache"]');
  assert(queue?.textContent.includes('3 jobs') && queue.textContent.includes('3 outside interval'),'queue count retains off-screen jobs');
  queue.click(); await frame();
  assert(snapshot.jobs.every(j=>representedIds().has(j.id)),'queue inspection reveals all recorded jobs including Unassigned');
  assert(viewer.visibleWindowMs>5000,'history inspection can span longer than the live overview');
  viewer.shadowRoot.querySelector('.inspection-jobs [data-result="older-2"]').click(); await frame();
  assert(snapshot.jobs.every(j=>representedIds().has(j.id)),'selecting an old job keeps queue context');
  viewer.followLive(); await frame();
  assert(viewer.shadowRoot.querySelector('[data-inspect-queue="cache"]').textContent.includes('3 jobs'),'history remains available after returning live');
});

await check('group click separates distinct timestamps and scrolls without changing channel or time facts', async () => {
  viewer.showOverview(); viewer.style.height='440px';
  const base=Date.parse(reference)-1000;
  const jobs=[0,1,40].map((offset,i)=>({id:`zoom-${i}`,queueId:'zoom',channel:0,state:'running',
    observedAt:new Date(base+offset).toISOString()}));
  viewer.setData({queues:[{id:'wide',maxParallelism:30},{id:'zoom',maxParallelism:1}],jobs}); viewer.setReferenceTime(reference); await frame();
  const root=viewer.shadowRoot, x=root.querySelector('.channel-line').getAttribute('x1');
  root.querySelector('.cluster').dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  assert(root.querySelectorAll('.timeline .job').length===3 && !root.querySelector('.timeline .cluster'),'one click separates every distinct time');
  assert(root.querySelector('.channel-line').getAttribute('x1')===x,'time zoom preserves channel width');
  const viewport=root.querySelector('.viewport');
  const bounds=viewport.getBoundingClientRect();
  assert([...root.querySelectorAll('.position-marker')].every(marker=>{
    const rect=marker.getBoundingClientRect(); return rect.left>=bounds.left+124 && rect.right<=bounds.right;
  }),'opening the inspector keeps the group channel visible horizontally');
  assert(viewport.scrollHeight>viewport.clientHeight,'the entire group has scrollable space');
  const centers=[...root.querySelectorAll('.position-marker')].map(n=>+n.getAttribute('y') + +n.getAttribute('height')/2);
  assert(Math.abs((centers[2]-centers[1])/(centers[1]-centers[0])-39)<0.001,'spacing preserves actual timestamp differences');
  viewport.scrollTop=100; viewport.dispatchEvent(new Event('scroll')); await frame();
  assert(root.querySelector('.time-ruler').style.transform===`translateY(${-viewport.scrollTop}px)`,'ruler follows vertical inspection scrolling');
  root.querySelector('.inspection-jobs [data-result="zoom-2"]').click(); await frame();
  assert(viewport.scrollTop>100,'selecting a member reveals its timeline position');
  assert(root.querySelectorAll('.timeline .job').length===3,'selection preserves the full group');
  viewer.showOverview(); await frame();
  assert(viewer.visibleWindowMs===5000 && viewport.scrollTop===0,'overview restores the five-second canvas');
  viewer.style.height='640px';
});

await check('group completion holds its square on the timeline until Follow live; click and zoom reveal its jobs', async () => {
  viewer.followLive();
  const base=Date.now()-1000;
  const snapshot={queues:[{id:'retained',name:'Retained',maxParallelism:1}],jobs:[0,1].map(i=>({
    id:`retained-${i}`,queueId:'retained',channel:0,state:'running',observedAt:new Date(base+i).toISOString()}))};
  viewer.setData(snapshot); await frame();
  const root=viewer.shadowRoot;
  assert(root.querySelector('.timeline .cluster.incomplete'),'unfinished group is neutral');
  const next=structuredClone(snapshot); next.jobs[0].state='completed'; viewer.setData(next); await frame();
  assert(root.querySelector('.timeline .cluster.incomplete'),'one completed member does not turn the group green');
  next.jobs[1].state='completed'; viewer.setData(next); await frame();
  const cluster=root.querySelector('.timeline .cluster.completed');
  assert(cluster && cluster.querySelector('.cluster-count').textContent==='2','completed group stays on the timeline');
  assert(getComputedStyle(cluster.querySelector('.cluster-box')).stroke===getComputedStyle(cluster.querySelector('.cluster-count')).fill,'outline and count share completion color');
  const heldReference=viewer.referenceTime, heldY=cluster.querySelector('.cluster-box').getAttribute('y');
  // A later snapshot must hold the existing interval before its group could scroll away.
  const now=Date.now;
  try { Date.now=()=>base+10000; viewer.setData(next); } finally { Date.now=now; }
  await frame();
  assert(!viewer.isFollowingLive && viewer.referenceTime===heldReference,'view holds instead of scrolling the group away');
  assert(root.querySelector('.cluster.completed .cluster-box').getAttribute('y')===heldY,'same completed square stays at its position');
  assert(!root.querySelector('.group-history'),'no separate group history');
  root.querySelector('.cluster').dispatchEvent(new MouseEvent('click',{bubbles:true})); await frame();
  assert(root.querySelectorAll('.timeline .job.completed').length===2,'click separates completed members on the same timeline');
  viewer.showOverview(); await frame();
  root.querySelector('[data-action="in"]').click(); await frame();
  assert(root.querySelectorAll('.timeline .job.completed').length===2,'zoom also separates the group without losing its jobs');
  root.querySelector('[data-action="in"]').click(); await frame();
  assert(root.querySelectorAll('.timeline .job.completed').length===2,'further zoom keeps all group members');
  try { Date.now=()=>base+10000; viewer.followLive(); } finally { Date.now=now; }
  await frame();
  assert(viewer.isFollowingLive && viewer.visibleWindowMs===5000,'Follow live explicitly resumes the latest five-second interval');
  assert(!root.querySelector('.timeline .cluster'),'explicit Follow live may move old groups out of the latest interval');
});

document.querySelector('#summary').textContent=`${passed} passed; ${failed} failed`;
document.documentElement.dataset.result = failed ? 'failed' : 'passed';
if(location.search.includes('automation')) await fetch('http://127.0.0.1:4178/__acceptance/done?id=standalone', {mode:'no-cors'});
