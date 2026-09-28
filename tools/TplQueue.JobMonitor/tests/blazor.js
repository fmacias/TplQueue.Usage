const frame=document.querySelector('iframe'), results=document.querySelector('#results');
const singleJob=new URLSearchParams(location.search).get('profile')==='single-job';
const expectedJobs=singleJob?6:18, completedPerQueue=singleJob?'2':'6';
let passed=0,failed=0, errors=[];
const wait=ms=>new Promise(r=>setTimeout(r,ms));
async function until(predicate) {
  for(let i=0;i<100;i++) { const value=predicate(); if(value) return value; await wait(100); }
  throw new Error(`Timed out waiting for the interactive Blazor circuit; Blazor=${typeof frame.contentWindow?.Blazor}; viewer=${!!viewer()}; shadow=${!!viewer()?.shadowRoot}; page=${frame.contentDocument?.body?.innerText?.slice(0, 300)}`);
}
async function check(name,fn) {
  const li=document.createElement('li');results.append(li);
  try{await fn();passed++;li.className='pass';li.textContent=`PASS ${name}`;}
  catch(e){failed++;li.className='fail';li.textContent=`FAIL ${name}: ${e.message}`;}
}
const assert=(x,message)=>{if(!x)throw new Error(message);};
const viewer=()=>frame.contentDocument?.querySelector('job-queue-timeline');
await check('observer snapshots render through the actual Blazor circuit',async()=>{
  await until(()=>viewer()?.shadowRoot?.querySelector('.mode')?.textContent.includes(`${expectedJobs} jobs`));
  frame.contentWindow.addEventListener('error',e=>errors.push(e.message));
  frame.contentWindow.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
  assert(frame.contentDocument.querySelectorAll('job-queue-timeline').length===1,'one monitor');
  assert(!frame.contentDocument.querySelector('.job-details-panel'),'no details');
});
await check('real channel values and selection round trip',async()=>{
  // Finite timer arrivals can expose all jobs while the last roots are still queued.
  // A queued job has no channel until Started; select after real execution completes.
  await until(()=>[...frame.contentDocument.querySelectorAll('[data-completed]')]
    .filter(q=>q.dataset.completed===completedPerQueue).length===3);
  const v=viewer(),input=v.shadowRoot.querySelector('input[type=search]');
  input.value='measurements';input.dispatchEvent(new Event('input',{bubbles:true}));
  const result=await until(()=>v.shadowRoot.querySelector('.results button'));
  result.click();await wait(200);
  assert(v.selectedJobId,'selected job');
  let selection;
  const membershipListener=e=>selection=e.detail;
  v.addEventListener('job-select',membershipListener);
  try { v.focusJob(v.selectedJobId); } finally { v.removeEventListener('job-select',membershipListener); }
  assert(selection?.rootJobIds?.length===1 && selection.rootJobId===selection.rootJobIds[0],
    'real simulation membership reaches browser selection');
  // The same selected job can also have an Unassigned enqueue-history marker.
  const nodes=[...v.shadowRoot.querySelectorAll('.selected title')];
  // A selected job may be represented by a collision group at overview scale.
  assert(nodes.some(node=>/(?:Channel: |channel )\d/.test(node.textContent)),
    `runtime channel is known; selected=${v.selectedJobId}; markers=${nodes.map(node=>node.textContent).join(' | ')}`);
});
await check('idle circuit and selection do not resend the projection snapshot',async()=>{
  await until(()=>[...frame.contentDocument.querySelectorAll('[data-completed]')]
    .filter(q=>q.dataset.completed===completedPerQueue).length===3);
  await wait(300);
  const v=viewer(),original=v.setData;
  let updates=0;
  v.setData=function(snapshot){updates++;return original.call(this,snapshot);};
  try {
    v.followLive(); await wait(100);
    const before=v.referenceTime;
    await wait(650);
    assert(v.referenceTime===before && updates===0,'idle circuit has no refresh');
    v.focusJob(v.selectedJobId); await wait(300);
    assert(updates===0,'selection callback must not resend unchanged data');
  } finally { v.setData=original; }
});
await check('navigation away and back disposes and reconnects one wrapper',async()=>{
  frame.contentWindow.Blazor.navigateTo('/Error');
  await until(()=>!viewer());
  frame.contentWindow.Blazor.navigateTo('/');
  await until(()=>viewer()?.shadowRoot?.querySelector('.mode')?.textContent.includes(`${expectedJobs} jobs`));
  const v=viewer(),input=v.shadowRoot.querySelector('input[type=search]');
  input.value='measurements';input.dispatchEvent(new Event('input',{bubbles:true}));
  const result=await until(()=>v.shadowRoot.querySelector('.results button'));
  let selected=0;v.addEventListener('job-select',()=>selected++);result.click();await wait(200);
  assert(selected===1,'one selection subscription');assert(errors.length===0,errors.join('; '));
  assert(getComputedStyle(frame.contentDocument.querySelector('#blazor-error-ui')).display==='none','no Blazor error banner');
});
await check('completed snapshots retain enqueue strips and enqueue-to-start relations',async()=>{
  const v=viewer();
  assert(v.shadowRoot.querySelectorAll('[data-unassigned-id]').length===3,'history strip for each runtime queue');
  v.showOverview();
  // Select real observer jobs, including clustered enqueue timestamps, through the circuit.
  const input=v.shadowRoot.querySelector('input[type=search]');
  input.value='measurements';input.dispatchEvent(new Event('input',{bubbles:true}));
  const entries=[...v.shadowRoot.querySelectorAll('.results button')];
  let connected=false;
  for(const entry of entries) {
    v.focusJob(entry.dataset.result); await wait(100);
    if(v.shadowRoot.querySelector('.assignment[marker-end]')) { connected=true; break; }
  }
  assert(connected,'recorded enqueue and Started positions are connected');
  assert(v.shadowRoot.querySelector('.mode').textContent.includes(`${expectedJobs} jobs`),'history markers do not inflate job counts');
  assert(errors.length===0,errors.join('; '));
});
if(singleJob) await check('each independent root has two positions and one identity on its runtime queue',async()=>{
  const v=viewer(),input=v.shadowRoot.querySelector('input[type=search]');
  input.value='Single job:';input.dispatchEvent(new Event('input',{bubbles:true}));
  const ids=[...v.shadowRoot.querySelectorAll('.results button')].map(button=>button.dataset.result);
  assert(new Set(ids).size===6,'six distinct one-job roots');
  const channels=new Map();
  for(const id of ids) {
    let selection;
    const listener=e=>selection=e.detail;
    v.addEventListener('job-select',listener);
    try { v.focusJob(id); } finally { v.removeEventListener('job-select',listener); }
    await wait(100);
    const markers=[...v.shadowRoot.querySelectorAll('[data-job-id]')].filter(node=>node.dataset.jobId===id);
    assert(markers.length===2,'one enqueue and one execution position for the same job');
    const titles=markers.map(node=>node.querySelector('title').textContent);
    assert(titles.some(text=>text.includes('Channel: unassigned')),'enqueue history stays Unassigned');
    const assigned=titles.find(text=>/Channel: \d/.test(text));
    assert(assigned && assigned.includes('timestampSource: Started'),'execution uses real Started position');
    assert(selection.rootJobId===id && selection.rootJobIds.length===1 && selection.rootJobIds[0]===id,
      'each root has independent composition identity');
    const queue=assigned.match(/Queue: (.+)/)[1],channel=assigned.match(/Channel: (\d+)/)[1];
    const seen=channels.get(queue)??[];seen.push(channel);channels.set(queue,seen);
  }
  assert(channels.size===3,'all runtime queues inspected');
  assert([...channels.values()].every(values=>values.length===2), 'two completed independent roots per queue');
  assert(v.shadowRoot.querySelector('.mode').textContent.includes('6 jobs'),'twelve position markers still count six jobs');
});
document.querySelector('#summary').textContent=`${passed} passed; ${failed} failed`;
document.documentElement.dataset.result=failed?'failed':'passed';
if(location.search.includes('automation')) await fetch('http://127.0.0.1:4178/__acceptance/done?id=blazor', {mode:'no-cors'});
