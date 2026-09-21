import '../src/job-queue-timeline.js';
import { sampleData, reference } from './data.js';
const timeline = document.querySelector('job-queue-timeline');
let snapshot, interval, next = 0;
function reset() {
  clearInterval(interval); interval = null; next = 0;
  document.querySelector('#arrivals').textContent = 'Start arrivals';
  snapshot = sampleData(); timeline.showOverview(); timeline.setData(snapshot); timeline.setReferenceTime(reference); timeline.clearFocus();
}
document.querySelector('#reset').addEventListener('click', reset);
document.querySelector('#arrivals').addEventListener('click', e => {
  if (interval) { clearInterval(interval); interval = null; e.target.textContent = 'Start arrivals'; return; }
  const shift = Date.now() - Date.parse(reference);
  snapshot = sampleData();
  delete snapshot.referenceTime;
  snapshot.jobs = snapshot.jobs.map(j => ({...j, observedAt:new Date(Date.parse(j.observedAt)+shift).toISOString()}));
  timeline.setData(snapshot); timeline.followLive(); e.target.textContent = 'Pause arrivals';
  interval = setInterval(() => {
    next++;
    snapshot.jobs.push({ id:`arrival-${next}`, name:`Live measurement ${next}`, description:'Synthetic demo arrival', queueId:'parallel',
      channel:next%3, observedAt:new Date().toISOString(), state:'completed', dependsOn:[], metadata:{sequence:next}, durationMs:100 });
    snapshot.jobs = snapshot.jobs.slice(-100);
    timeline.setData(snapshot);
  },1000);
});
reset();
