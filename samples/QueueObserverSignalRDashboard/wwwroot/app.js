const waitRunButton = document.getElementById("wait-run");
const cancelRunButton = document.getElementById("cancel-run");
const payloadWaitRunButton = document.getElementById("payload-wait-run");
const payloadCancelRunButton = document.getElementById("payload-cancel-run");
const statusLine = document.getElementById("status-line");
const activeRunPill = document.getElementById("active-run-pill");
const runsContainer = document.getElementById("runs");
const eventsContainer = document.getElementById("events");
const payloadBadge = document.getElementById("payload-badge");
const payloadMeta = document.getElementById("payload-meta");
const payloadContent = document.getElementById("payload-content");

const runs = new Map();
const eventsByRun = new Map();
let activeRunId = null;
let selectedEventKey = null;

function setStatus(message) {
    statusLine.textContent = message;
}

function isTerminal(status) {
    return status === "Completed" || status === "Canceled" || status === "Failed";
}

function setButtonsDisabled(disabled) {
    waitRunButton.disabled = disabled;
    cancelRunButton.disabled = disabled;
    payloadWaitRunButton.disabled = disabled;
    payloadCancelRunButton.disabled = disabled;
}

function getScenarioLabel(scenario) {
    return scenario === "payload" ? "Payload" : "Metadata";
}

function renderRuns() {
    const orderedRuns = [...runs.values()].sort((left, right) =>
        new Date(right.startedUtc) - new Date(left.startedUtc));

    runsContainer.innerHTML = "";

    if (orderedRuns.length === 0) {
        runsContainer.innerHTML = "<p class='empty'>No runs started yet.</p>";
        activeRunPill.textContent = "No active run";
        return;
    }

    orderedRuns.forEach((run) => {
        const card = document.createElement("button");
        card.type = "button";
        card.className = `run-card ${run.runId === activeRunId ? "selected" : ""}`;
        card.addEventListener("click", async () => {
            activeRunId = run.runId;
            selectedEventKey = null;
            await ensureEventsLoaded(run.runId);
            renderRuns();
            renderEvents();
        });

        card.innerHTML = `
            <span class="run-mode">${getScenarioLabel(run.scenario)} / ${run.mode}</span>
            <strong>${run.status}</strong>
            <span>${run.queueName || "Queue pending"}</span>
            <span>${new Date(run.startedUtc).toLocaleTimeString()}</span>
            <small>${run.runId}</small>
        `;

        runsContainer.appendChild(card);
    });

    const activeRun = orderedRuns.find((run) => run.runId === activeRunId && !isTerminal(run.status))
        ?? orderedRuns.find((run) => !isTerminal(run.status))
        ?? orderedRuns[0];

    activeRunPill.textContent = activeRun
        ? `Selected: ${getScenarioLabel(activeRun.scenario)} / ${activeRun.mode} / ${activeRun.status}`
        : "No active run";
}

function renderEvents() {
    const selectedEvents = activeRunId ? (eventsByRun.get(activeRunId) || []) : [];
    const sortedEvents = selectedEvents
        .slice()
        .sort((left, right) => left.sequence - right.sequence);

    eventsContainer.innerHTML = "";

    if (sortedEvents.length === 0) {
        eventsContainer.innerHTML = "<tr><td colspan='9' class='empty-cell'>No projected DTO events for the selected run yet.</td></tr>";
        renderPayloadInspector(null);
        return;
    }

    const selectedEvent = sortedEvents.find((item) => eventKey(item) === selectedEventKey) || null;

    sortedEvents.forEach((queueEvent) => {
        const row = document.createElement("tr");
        const isSelected = selectedEventKey === eventKey(queueEvent);
        row.className = `event-row ${isSelected ? "selected" : ""} ${queueEvent.hasPayloadSnapshot ? "has-payload" : ""}`;
        row.addEventListener("click", () => {
            selectedEventKey = eventKey(queueEvent);
            renderEvents();
        });

        row.innerHTML = `
            <td>${queueEvent.sequence}</td>
            <td><span class="scenario-chip">${getScenarioLabel(queueEvent.scenario)}</span></td>
            <td><span class="status-chip status-${queueEvent.status.toLowerCase()}">${queueEvent.status}</span></td>
            <td>${queueEvent.name}</td>
            <td>${queueEvent.hasPayloadSnapshot ? "<span class='payload-chip'>JSON</span>" : "<span class='payload-chip empty'>None</span>"}</td>
            <td>${queueEvent.retryCount}</td>
            <td>${queueEvent.queueId}</td>
            <td>${new Date(queueEvent.timestampUtc).toLocaleTimeString()}</td>
            <td>${queueEvent.exceptionMessage || ""}</td>
        `;
        eventsContainer.appendChild(row);
    });

    renderPayloadInspector(selectedEvent || sortedEvents[sortedEvents.length - 1]);
}

function renderPayloadInspector(queueEvent) {
    if (!queueEvent) {
        payloadBadge.textContent = "Click an event row";
        payloadMeta.textContent = "The payload queue publishes detached JSON snapshots on terminal DataJob events.";
        payloadContent.textContent = "No payload snapshot selected.";
        return;
    }

    payloadMeta.textContent = `${getScenarioLabel(queueEvent.scenario)} / ${queueEvent.name} / ${queueEvent.status}`;

    if (!queueEvent.hasPayloadSnapshot || !queueEvent.serializedPayload) {
        payloadBadge.textContent = "No payload snapshot";
        payloadContent.textContent = "This event carried metadata only.";
        return;
    }

    payloadBadge.textContent = queueEvent.payloadHandlerKey || queueEvent.payloadTypeName || "Payload snapshot";
    payloadContent.textContent = queueEvent.serializedPayload;
}

async function ensureEventsLoaded(runId) {
    if (!runId || eventsByRun.has(runId)) {
        return;
    }

    const response = await fetch(`/api/sample/runs/${runId}/events`);
    const payload = await response.json();
    eventsByRun.set(runId, payload);
}

function upsertRun(run) {
    runs.set(run.runId, run);

    if (!activeRunId || !runs.has(activeRunId) || !isTerminal(run.status)) {
        activeRunId = run.runId;
        selectedEventKey = null;
    }

    setButtonsDisabled([...runs.values()].some((item) => !isTerminal(item.status)));
    renderRuns();
}

function appendEvent(queueEvent) {
    const current = eventsByRun.get(queueEvent.runId) || [];
    current.push(queueEvent);
    eventsByRun.set(queueEvent.runId, current);

    if (!activeRunId) {
        activeRunId = queueEvent.runId;
    }

    if (activeRunId === queueEvent.runId) {
        if (!selectedEventKey && queueEvent.hasPayloadSnapshot) {
            selectedEventKey = eventKey(queueEvent);
        }

        renderEvents();
    }
}

async function loadInitialState() {
    const response = await fetch("/api/sample/runs");
    const runSnapshots = await response.json();

    runSnapshots.forEach((run) => {
        runs.set(run.runId, run);
    });

    activeRunId = runSnapshots.length > 0 ? runSnapshots[0].runId : null;
    setButtonsDisabled(runSnapshots.some((item) => !isTerminal(item.status)));

    if (activeRunId) {
        await ensureEventsLoaded(activeRunId);
    }

    renderRuns();
    renderEvents();
}

async function startRun(routeBase, mode, label) {
    setStatus(`Starting ${label} ${mode} run...`);

    const response = await fetch(`${routeBase}/${mode}`, {
        method: "POST"
    });

    const payload = await response.json();

    if (!response.ok) {
        setStatus(payload.message || "Unable to start the requested run.");
        return;
    }

    upsertRun(payload);
    eventsByRun.set(payload.runId, []);
    activeRunId = payload.runId;
    selectedEventKey = null;
    renderEvents();
    setStatus(`Run ${payload.runId} started for the ${label} queue in ${payload.mode} mode.`);
}

function eventKey(queueEvent) {
    return `${queueEvent.runId}:${queueEvent.sequence}`;
}

waitRunButton.addEventListener("click", () => startRun("/api/sample/runs", "wait", "metadata"));
cancelRunButton.addEventListener("click", () => startRun("/api/sample/runs", "cancel", "metadata"));
payloadWaitRunButton.addEventListener("click", () => startRun("/api/sample/payload-runs", "wait", "payload"));
payloadCancelRunButton.addEventListener("click", () => startRun("/api/sample/payload-runs", "cancel", "payload"));

async function bootstrap() {
    await loadInitialState();

    const connection = new signalR.HubConnectionBuilder()
        .withUrl("/hubs/queue-events")
        .withAutomaticReconnect()
        .build();

    connection.on("runUpdated", (run) => {
        upsertRun(run);
        if (activeRunId === run.runId && isTerminal(run.status)) {
            setStatus(`Run ${run.runId} finished with status ${run.status}.`);
        }
    });

    connection.on("jobEvent", (queueEvent) => {
        appendEvent(queueEvent);
        setStatus(`Observed ${getScenarioLabel(queueEvent.scenario)} ${queueEvent.status} for ${queueEvent.name}.`);
    });

    await connection.start();
    setStatus("SignalR connection established. Start a run to watch projected queue DTOs.");
}

bootstrap().catch((error) => {
    console.error(error);
    setStatus("SignalR dashboard failed to initialize.");
});
