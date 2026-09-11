const instances = new Map();
const runningRefreshMilliseconds = 750;
const minimumVisibleDurationMilliseconds = 50;
let generatedElementId = 0;

const readOnlyOptions = Object.freeze({
    add: false,
    updateTime: false,
    updateGroup: false,
    remove: false,
    overrideItems: false
});

const neverDraggableOptions = Object.freeze({
    item: false,
    range: false
});

function getVisApi() {
    const visApi = globalThis.vis;
    if (!visApi || typeof visApi.DataSet !== "function" || typeof visApi.Timeline !== "function") {
        throw new Error("vis-timeline 8.5.2 must be loaded before tplqueueTimeline.js.");
    }

    return visApi;
}

function resolveElement(elementOrId) {
    const element = typeof elementOrId === "string"
        ? document.getElementById(elementOrId)
        : elementOrId;

    if (!element) {
        throw new Error("The timeline host element was not found.");
    }

    if (!element.id) {
        generatedElementId += 1;
        element.id = `tplqueue-timeline-${generatedElementId}`;
    }

    return element;
}

function parseDate(value, propertyName, itemId) {
    const result = value instanceof Date ? new Date(value.getTime()) : new Date(value);
    if (Number.isNaN(result.getTime())) {
        throw new Error(`Timeline item '${itemId}' has an invalid ${propertyName}.`);
    }

    return result;
}

function normalizeGroup(group) {
    return {
        id: String(group.id),
        content: String(group.content),
        order: Number.isFinite(group.order) ? group.order : 0
    };
}

function minimumVisibleEnd(start, candidate) {
    return candidate.getTime() > start.getTime()
        ? candidate
        : new Date(start.getTime() + minimumVisibleDurationMilliseconds);
}

function normalizeItem(item) {
    const id = String(item.id);
    const start = parseDate(item.start, "start time", id);
    const isRunning = item.isRunning === true;
    const normalized = {
        id,
        group: String(item.group),
        content: String(item.content),
        title: String(item.title),
        start,
        type: isRunning ? "range" : item.type === "range" ? "range" : "point",
        className: String(item.className),
        isRunning
    };

    if (isRunning) {
        normalized.end = minimumVisibleEnd(start, new Date());
    } else if (normalized.type === "range" && item.end != null) {
        normalized.end = minimumVisibleEnd(start, parseDate(item.end, "end time", id));
    } else {
        normalized.type = "point";
    }

    return normalized;
}

function reconcile(dataSet, values) {
    const ids = new Set(values.map(value => value.id));
    const removedIds = dataSet.getIds().filter(id => !ids.has(id));

    if (values.length > 0) {
        dataSet.update(values);
    }

    if (removedIds.length > 0) {
        dataSet.remove(removedIds);
    }
}

function refreshRunningItems(instance) {
    if (instance.disposed) {
        return;
    }

    const now = new Date();
    const updates = [];
    for (const item of instance.sourceItems.values()) {
        if (item.isRunning) {
            updates.push({
                id: item.id,
                end: minimumVisibleEnd(item.start, now)
            });
        }
    }

    if (updates.length > 0) {
        instance.items.update(updates);
    }
}

function synchronizeRunningTimer(instance) {
    const hasRunningItems = Array.from(instance.sourceItems.values())
        .some(item => item.isRunning);

    if (hasRunningItems && instance.runningTimer == null) {
        instance.runningTimer = globalThis.setInterval(
            () => refreshRunningItems(instance),
            runningRefreshMilliseconds);
    } else if (!hasRunningItems && instance.runningTimer != null) {
        globalThis.clearInterval(instance.runningTimer);
        instance.runningTimer = null;
    }
}

function fitInitialItems(instance) {
    if (!instance.initialFitComplete && instance.sourceItems.size > 0) {
        instance.timeline.fit({ animation: false });
        instance.initialFitComplete = true;
    }
}

function reconcileInstance(instance, groups, items) {
    const normalizedGroups = (groups ?? []).map(normalizeGroup);
    const normalizedItems = (items ?? []).map(normalizeItem);

    instance.sourceItems = new Map(normalizedItems.map(item => [item.id, item]));
    reconcile(instance.groups, normalizedGroups);
    reconcile(instance.items, normalizedItems);
    synchronizeRunningTimer(instance);
    fitInitialItems(instance);
}

function getInstance(elementId) {
    return instances.get(String(elementId));
}

export function create(elementOrId, groups, items, options = {}, dotNetReference = null) {
    const element = resolveElement(elementOrId);
    const existing = getInstance(element.id);

    if (existing && existing.element === element) {
        existing.dotNetReference = dotNetReference ?? existing.dotNetReference;
        reconcileInstance(existing, groups, items);
        return element.id;
    }

    if (existing) {
        dispose(element.id);
    }

    const visApi = getVisApi();
    const groupDataSet = new visApi.DataSet();
    const itemDataSet = new visApi.DataSet();
    const timelineOptions = {
        stack: true,
        selectable: true,
        multiselect: false,
        zoomable: true,
        moveable: true,
        showCurrentTime: true,
        orientation: "top",
        groupOrder: "order",
        ...(options ?? {}),
        editable: readOnlyOptions,
        itemsAlwaysDraggable: neverDraggableOptions
    };

    const timeline = new visApi.Timeline(element, itemDataSet, groupDataSet, timelineOptions);
    const instance = {
        element,
        groups: groupDataSet,
        items: itemDataSet,
        timeline,
        dotNetReference,
        sourceItems: new Map(),
        initialFitComplete: false,
        runningTimer: null,
        disposed: false,
        selectionHandler: null
    };

    instance.selectionHandler = event => {
        const selectedItemId = event.items.length > 0 ? String(event.items[0]) : null;
        const receiver = instance.dotNetReference;
        if (!instance.disposed && receiver) {
            receiver.invokeMethodAsync("OnTimelineItemSelected", selectedItemId)
                .catch(error => {
                    if (!instance.disposed) {
                        console.error("The timeline selection callback failed.", error);
                    }
                });
        }
    };

    timeline.on("select", instance.selectionHandler);
    instances.set(element.id, instance);
    reconcileInstance(instance, groups, items);
    return element.id;
}

export function update(elementId, groups, items) {
    const instance = getInstance(elementId);
    if (!instance || instance.disposed) {
        return false;
    }

    reconcileInstance(instance, groups, items);
    return true;
}

export function fit(elementId) {
    const instance = getInstance(elementId);
    if (!instance || instance.disposed || instance.sourceItems.size === 0) {
        return false;
    }

    instance.timeline.fit({ animation: true });
    return true;
}

export function dispose(elementId) {
    const instance = getInstance(elementId);
    if (!instance) {
        return;
    }

    instance.disposed = true;
    if (instance.runningTimer != null) {
        globalThis.clearInterval(instance.runningTimer);
        instance.runningTimer = null;
    }

    if (instance.selectionHandler) {
        instance.timeline.off("select", instance.selectionHandler);
    }

    instance.timeline.destroy();
    instance.groups.clear();
    instance.items.clear();
    instance.dotNetReference = null;
    instance.sourceItems.clear();
    instances.delete(String(elementId));
}
