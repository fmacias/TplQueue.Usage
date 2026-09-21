import '../src/job-queue-timeline.js';
import { sampleData, reference } from './data.js';
const timeline = document.querySelector('job-queue-timeline');
let snapshot, interval, next = 0, pending = null;
function reset() {
  clearInterval(interval); interval = null; next = 0; pending = null;
  document.querySelector('#arrivals').textContent = 'Start arrivals';
  snapshot = sampleData(); timeline.showOverview(); timeline.setData(snapshot); timeline.setReferenceTime(reference); timeline.clearFocus();
}
document.querySelector('#reset').addEventListener('click', reset);
document.querySelector('#arrivals').addEventListener('click', e => {
  if (interval) { clearInterval(interval); interval = null; e.target.textContent = 'Start arrivals'; return; }
  const shift = Date.now() - Date.parse(reference);
  pending = null;
  snapshot = sampleData();
  delete snapshot.referenceTime;
  snapshot.jobs = snapshot.jobs.map(j => ({...j, observedAt:new Date(Date.parse(j.observedAt)+shift).toISOString()}));
  timeline.setData(snapshot); timeline.followLive(); e.target.textContent = 'Pause arrivals';
  interval = setInterval(() => {
    // Synthetic backend-style transition: keep identity and enqueue metadata, then
    // place the job at its channel acquisition time in the next snapshot.
    if (pending) {
      pending.channel = next % 3; pending.observedAt = new Date().toISOString();
      pending.state = 'completed'; pending.metadata.timestampSource = 'Started'; pending.durationMs = 0;
    }
    next++;
    const enqueuedAt = new Date().toISOString();
    pending = { id:`arrival-${next}`, name:`Live measurement ${next}`, description:'Synthetic demo arrival', queueId:'parallel',
      channel:null, observedAt:enqueuedAt, state:'waiting', dependsOn:[],
      metadata:{sequence:next,enqueuedAt,timestampSource:'Enqueued'}, durationMs:null };
    snapshot.jobs.push(pending);
    snapshot.jobs = snapshot.jobs.slice(-100);
    timeline.setData(snapshot);
  },1000);
});
reset();
