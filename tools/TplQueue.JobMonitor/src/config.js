export const defaults = Object.freeze({ scale: 1, windowMs: 5000, channelWidth: 40,
  minChannelWidth: 40, minMarkerSize: 2, maxMarkerSize: 12, targetSize: 24,
  clusterSize: 28, markerGap: 8, headerHeight: 56, bottomPadding: 20,
  gutter: 124, queuePadding: 8, liveLagMs: 0, maxSearchResults: 30 });

export function options(input = {}) {
  const value = { ...defaults, ...input };
  for (const key of Object.keys(defaults))
    if (!Number.isFinite(value[key]) || value[key] < (key === 'liveLagMs' ? -60000 : 0))
      throw new TypeError(`Invalid option: ${key}`);
  value.windowMs = Math.max(1, Math.min(5000, value.windowMs));
  value.scale = Math.max(1, Math.min(value.windowMs, value.scale));
  value.minMarkerSize = Math.max(2, value.minMarkerSize);
  value.maxMarkerSize = Math.max(value.minMarkerSize, value.maxMarkerSize);
  value.targetSize = Math.max(24, value.targetSize, value.maxMarkerSize + 8);
  value.clusterSize = Math.max(28, value.clusterSize, value.targetSize);
  value.markerGap = Math.max(8, value.markerGap);
  value.minChannelWidth = Math.max(40, value.minChannelWidth, value.clusterSize + value.markerGap);
  value.gutter = Math.max(124, value.gutter);
  value.headerHeight = Math.max(56, value.headerHeight);
  value.bottomPadding = Math.max(value.clusterSize / 2 + 4, value.bottomPadding);
  value.queuePadding = Math.max(8, value.queuePadding);
  return value;
}
