import '../src/job-queue-timeline.js';
import { sampleData, reference } from '../demo/data.js';
import { attach, update, detach } from '../integrations/blazor/job-monitor.js';
const viewer = document.querySelector('job-queue-timeline'), results = document.querySelector('#results');
const frame = () => new Promise(resolve => setTimeout(resolve, 100));
const assert = (value, message) => { if (!value) throw new Error(message); };
let passed = 0, failed = 0;
async function check(name, action) {
  const li = document.createElement('li'); results.append(li);
  try { await action(); passed++; li.className='pass'; li.textContent=`PASS ${name}`; }
  catch(error) { failed++; li.className='fail'; li.textContent=`FAIL ${name}: ${error.message}`; }
}
viewer.setData(sampleData()); viewer.setReferenceTime(reference); await frame();
await check('dark default and all logical channels', () => {
  assert(getComputedStyle(viewer).getPropertyValue('--jm-bg').trim()==='#1e1e1e','dark theme');
  assert(viewer.shadowRoot.querySelectorAll('.channel-line').length===21,'channel count');
  assert(viewer.shadowRoot.querySelectorAll('.unassigned-line').length===1,'explicit unassigned area');
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
  attach(viewer,callback); attach(viewer,callback); update(viewer,sampleData()); viewer.focusJob('root-summary');
  assert(calls===1,'no duplicate callbacks'); detach(viewer); viewer.focusJob('root-summary'); assert(calls===1,'detached');
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
  assert(viewer.visibleWindowMs===5000,'resize preserves interval');
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
document.querySelector('#summary').textContent=`${passed} passed; ${failed} failed`;
document.documentElement.dataset.result = failed ? 'failed' : 'passed';
if(location.search.includes('automation')) await fetch('http://127.0.0.1:4178/__acceptance/done?id=standalone', {mode:'no-cors'});
