export const reference = '2026-09-17T12:00:00.000Z';
export function sampleData() {
  const queues = [ { id:'fifo', name:'FifoQ', maxParallelism:1 }, { id:'parallel', name:'ParallelQ', maxParallelism:3 },
    { id:'cache', name:'CacheQ', maxParallelism:2 }, { id:'dense', name:'DenseQ', maxParallelism:15 } ];
  const job = (id, queueId, channel, seconds, state, dependsOn = [], extra = {}) => ({
    id, queueId, channel, name: id.replaceAll('-', ' '), description:'Synthetic measurement processing',
    observedAt: new Date(Date.parse(reference) + seconds * 1000).toISOString(), state, dependsOn,
    rootJobId:'root-summary', durationMs: state === 'completed' ? 380 : null,
    metadata:{ handler:'sample.measurement.v1', batch:'deterministic-42' }, ...extra });
  const jobs = [job('ingest-measurements','fifo',0,-4.8,'completed'), job('validate-measurements','fifo',0,-3.4,'completed',['ingest-measurements']),
    job('normalize-units','parallel',0,-2.8,'completed',['validate-measurements']),
    job('filter-outliers','parallel',1,-2.8,'retried',['validate-measurements']),
    job('calculate-average','parallel',2,-2.8,'failed',['validate-measurements']),
    job('root-summary','cache',0,-1.5,'running',['normalize-units','filter-outliers','calculate-average'],{isRoot:true}),
    job('waiting-batch','cache',null,-1,'waiting',[],{rootJobId:null}),
    job('cancelled-batch','cache',1,-.3,'canceled',[],{rootJobId:null})];
  for (let i=0;i<15;i++) jobs.push(job(`dense-${i}`,'dense',i,(i%3)-3,['running','completed','retried'][i%3],[],{rootJobId:null}));
  jobs.push(job('dense-follow-up','dense',0,-2.99,'completed',['dense-0'],{rootJobId:null}));
  jobs.push(job('same-time-example','dense',1,-2,'completed',[],{rootJobId:null}));
  return { queues, jobs, referenceTime:reference };
}
