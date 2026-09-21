const frame=document.querySelector('iframe'), results=document.querySelector('#results');
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
  await until(()=>viewer()?.shadowRoot?.querySelector('.mode')?.textContent.includes('18 jobs'));
  frame.contentWindow.addEventListener('error',e=>errors.push(e.message));
  frame.contentWindow.addEventListener('unhandledrejection',e=>errors.push(String(e.reason)));
  assert(frame.contentDocument.querySelectorAll('job-queue-timeline').length===1,'one monitor');
  assert(!frame.contentDocument.querySelector('.job-details-panel'),'no details');
});
await check('real channel values and selection round trip',async()=>{
  const v=viewer(),input=v.shadowRoot.querySelector('input[type=search]');
  input.value='measurements';input.dispatchEvent(new Event('input',{bubbles:true}));
  const result=await until(()=>v.shadowRoot.querySelector('.results button'));
  result.click();await wait(200);
  assert(v.selectedJobId,'selected job');
  const node=v.shadowRoot.querySelector('.selected title');
  assert(node && /Channel: \d/.test(node.textContent),'runtime channel is known');
});
await check('navigation away and back disposes and reconnects one wrapper',async()=>{
  frame.contentWindow.Blazor.navigateTo('/Error');
  await until(()=>!viewer());
  frame.contentWindow.Blazor.navigateTo('/');
  await until(()=>viewer()?.shadowRoot?.querySelector('.mode')?.textContent.includes('18 jobs'));
  const v=viewer(),input=v.shadowRoot.querySelector('input[type=search]');
  input.value='measurements';input.dispatchEvent(new Event('input',{bubbles:true}));
  const result=await until(()=>v.shadowRoot.querySelector('.results button'));
  let selected=0;v.addEventListener('job-select',()=>selected++);result.click();await wait(200);
  assert(selected===1,'one selection subscription');assert(errors.length===0,errors.join('; '));
  assert(getComputedStyle(frame.contentDocument.querySelector('#blazor-error-ui')).display==='none','no Blazor error banner');
});
document.querySelector('#summary').textContent=`${passed} passed; ${failed} failed`;
document.documentElement.dataset.result=failed?'failed':'passed';
if(location.search.includes('automation')) await fetch('http://127.0.0.1:4178/__acceptance/done?id=blazor', {mode:'no-cors'});
