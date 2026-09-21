import '../../src/job-queue-timeline.js';
const connections = new WeakMap();
export function attach(element, callback) {
  detach(element);
  const connection = { active: true };
  connection.listener = event => {
    if (connection.active) callback.invokeMethodAsync('SelectJob', event.detail.jobId).catch(error => {
      if (connection.active) console.debug('Job selection callback unavailable', error);
    });
  };
  element.addEventListener('job-select', connection.listener);
  connections.set(element, connection);
}
export function update(element, snapshot) { if (connections.has(element)) element.setData(snapshot); }
export function detach(element) {
  const connection = connections.get(element);
  if (!connection) return;
  connection.active = false;
  element.removeEventListener('job-select', connection.listener);
  connections.delete(element);
}
