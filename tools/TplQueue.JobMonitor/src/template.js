export const template = `
<div class="monitor">
  <div class="toolbar">
    <label class="search-label">Find job <input type="search" placeholder="Name, ID or description" aria-label="Find job"></label>
    <button type="button" data-action="clear">Clear focus</button>
    <div class="scale"><button type="button" data-action="out" aria-label="Show a longer interval">−</button>
      <output class="scale-value" aria-label="Visible time interval">5 s</output><button type="button" data-action="in" aria-label="Inspect a shorter interval">+</button></div>
    <button type="button" data-action="overview" hidden>Back to 5 seconds</button>
    <button type="button" data-action="pause">Pause</button>
    <button type="button" data-action="live">Follow live</button>
    <label>Window end (UTC) <input class="reference-input" type="datetime-local" step="0.001" aria-label="Window end datetime UTC"></label>
  </div>
  <div class="results" aria-label="Matching jobs"></div>
  <div class="viewport-row">
    <div class="viewport" tabindex="0" aria-label="Queue timeline. Scroll horizontally for queues; use arrow or page keys for history.">
      <svg class="timeline" role="group" aria-label="Jobs and dependencies"></svg>
    </div>
    <svg class="time-ruler" role="img" aria-label="UTC time ruler"></svg>
    <section class="inspection" aria-label="Jobs in the selected interval" hidden>
      <div class="inspection-toolbar"><h2 class="inspection-heading" tabindex="-1"></h2>
        <button type="button" data-action="close-inspection" aria-label="Close interval job list">×</button></div>
      <p class="inspection-note"></p>
      <div class="inspection-jobs"></div>
      <button type="button" data-action="overview">Back to 5 seconds</button>
    </section>
    <div class="history" tabindex="0" role="region" aria-label="Scroll execution history vertically"><div></div></div>
  </div>
  <footer><span class="mode" role="status"></span><span class="legend">W Waiting · ▶ Running · ✓ Completed · ↻ Retried · × Failed · − Cancelled</span>
    <span>Square center = exact time · Number = grouped jobs · U = Unassigned</span></footer>
  <div class="accessible-info" role="status"></div>
</div>`;

