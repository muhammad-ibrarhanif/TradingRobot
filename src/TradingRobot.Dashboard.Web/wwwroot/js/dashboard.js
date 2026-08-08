// Dashboard.Web chart client — plain JS, no build step, per
// Dashboard-Frontend-Requirements.md ("TypeScript SPA" was rejected in favor of
// MVC + Razor + plain JS). Talks to /api/marketdata/* for REST data and
// /hubs/candles (SignalR) for live candle updates.

const appEl = document.getElementById('app');
let currentSymbol = appEl.dataset.defaultSymbol;
let currentInterval = '1h';

// null = live mode (last N candles up to now, live SignalR updates, signals from
// the Redis Stream). {from, to} = historical mode (a fixed past range, no live
// subscription, signals computed on demand for that range) — see
// Dashboard-Frontend-Requirements.md "date range picker".
let historicalRange = null;

const chart = LightweightCharts.createChart(document.getElementById('chart'), {
  layout: { background: { color: '#0d1117' }, textColor: '#c9d1d9' },
  grid: { vertLines: { color: '#21262d' }, horzLines: { color: '#21262d' } },
  timeScale: { timeVisible: true },
});
const candleSeries = chart.addCandlestickSeries();

const overlay = document.getElementById('draw-overlay');
const overlayCtx = overlay.getContext('2d');

// --- layout / resize -------------------------------------------------------

function resizeOverlay() {
  const rect = document.getElementById('chart').getBoundingClientRect();
  overlay.width = rect.width;
  overlay.height = rect.height;
  chart.applyOptions({ width: rect.width });
  redrawOverlay();
}
new ResizeObserver(resizeOverlay).observe(document.querySelector('.chart-container'));
chart.timeScale().subscribeVisibleTimeRangeChange(redrawOverlay);

// --- symbol dropdown --------------------------------------------------------

async function loadSymbols() {
  try {
    const res = await fetch('/api/marketdata/symbols');
    const symbols = await res.json();
    const select = document.getElementById('symbol-select');
    select.innerHTML = '';
    for (const s of symbols) {
      const opt = document.createElement('option');
      opt.value = s;
      opt.textContent = s;
      if (s === currentSymbol) opt.selected = true;
      select.appendChild(opt);
    }
  } catch {
    // Symbol list is a convenience dropdown, not required for the chart itself
    // to work — fail quietly and keep whatever's already in the <select>.
  }
}

document.getElementById('symbol-select').addEventListener('change', e => {
  currentSymbol = e.target.value;
  refreshAll();
});

// --- timeframe buttons -------------------------------------------------------

document.getElementById('timeframes').addEventListener('click', e => {
  const btn = e.target.closest('.tf-btn');
  if (!btn) return;
  document.querySelectorAll('.tf-btn').forEach(b => b.classList.remove('active'));
  btn.classList.add('active');
  currentInterval = btn.dataset.interval;
  refreshAll();
});
// Highlight whichever button actually matches currentInterval (default '1h') —
// not just the first button in the list, which would show "1m" active while
// 1h candles are what's actually loaded.
document.querySelector(`.tf-btn[data-interval="${currentInterval}"]`)?.classList.add('active');

// --- candles / patterns / signals -------------------------------------------

function toChartCandle(c) {
  return { time: Math.floor(new Date(c.openTime).getTime() / 1000), open: c.open, high: c.high, low: c.low, close: c.close };
}

let patterns = []; // currently-visible patterns (all of them in live mode; only the ones "replayed so far" in historical mode)

// Builds the same marker shape both live mode and replay use — one color per
// strategy, strategy-name prefix only when more than one is actually present
// (see Dashboard-Frontend-Requirements.md fix notes).
function buildSignalMarkers(signals) {
  const colors = ['#3fb950', '#f85149', '#58a6ff', '#d29922', '#bc8cff'];
  const byStrategy = new Map();
  for (const s of signals) {
    if (!byStrategy.has(s.strategyName)) byStrategy.set(s.strategyName, byStrategy.size);
  }
  const multiStrategy = byStrategy.size > 1;

  return signals.map(s => {
    const color = colors[byStrategy.get(s.strategyName) % colors.length];
    const sideText = s.side === 0 ? 'BUY' : 'SELL';
    return {
      time: Math.floor(new Date(s.generatedAt).getTime() / 1000),
      position: s.side === 0 ? 'belowBar' : 'aboveBar',
      color,
      shape: s.side === 0 ? 'arrowUp' : 'arrowDown',
      text: multiStrategy ? `${s.strategyName}: ${sideText}` : sideText,
    };
  }).sort((a, b) => a.time - b.time);
}

// --- live mode: one fetch, everything shown immediately ---------------------

async function loadLiveCandles() {
  const res = await fetch(`/api/marketdata/candles?symbol=${currentSymbol}&interval=${currentInterval}&limit=300`);
  candleSeries.setData((await res.json()).map(toChartCandle));
}

async function loadLivePatterns() {
  const res = await fetch(`/api/marketdata/patterns?symbol=${currentSymbol}&interval=${currentInterval}&limit=300`);
  patterns = await res.json();
  redrawOverlay();
}

async function loadLiveSignals() {
  const res = await fetch(`/api/marketdata/signals?symbol=${currentSymbol}&count=100`);
  candleSeries.setMarkers(buildSignalMarkers(await res.json()));
}

// --- historical mode: fetch the whole range once, then play it back candle
// by candle instead of dumping everything on the chart at once. Patterns and
// signals are only revealed once playback actually reaches the candle(s) that
// produced them — so watching a past range unfold looks the same as watching
// it live would have, rather than seeing every signal for the week at once.

let replayCandles = [];
let replayPatterns = [];
let replaySignals = [];
let replayIndex = 0;
let replayTimer = null;
let replaySpeedMs = 150; // ms per candle — see #replay-speed

async function loadHistoricalReplayData() {
  const q = `symbol=${currentSymbol}&interval=${currentInterval}&from=${historicalRange.from}&to=${historicalRange.to}`;
  const [candlesRes, patternsRes, signalsRes] = await Promise.all([
    fetch(`/api/marketdata/candles?${q}`),
    fetch(`/api/marketdata/patterns?${q}`),
    fetch(`/api/marketdata/signals?${q}`),
  ]);
  replayCandles = await candlesRes.json();
  replayPatterns = await patternsRes.json();
  replaySignals = await signalsRes.json();
  replayIndex = 0;

  candleSeries.setData([]);
  candleSeries.setMarkers([]);
  patterns = [];
  redrawOverlay();
}

function stepReplay() {
  if (replayIndex >= replayCandles.length) {
    pauseReplay();
    return;
  }

  const candle = replayCandles[replayIndex];
  candleSeries.update(toChartCandle(candle));
  const cutoff = Math.floor(new Date(candle.openTime).getTime() / 1000);

  patterns = replayPatterns.filter(p => Math.floor(new Date(p.endTime).getTime() / 1000) <= cutoff);
  const revealedSignals = replaySignals.filter(s => Math.floor(new Date(s.generatedAt).getTime() / 1000) <= cutoff);
  candleSeries.setMarkers(buildSignalMarkers(revealedSignals));
  redrawOverlay();

  replayIndex++;
  updateReplayProgress();
}

function startReplay() {
  clearInterval(replayTimer);
  replayTimer = setInterval(stepReplay, replaySpeedMs);
  setReplayControlsState(true);
}

function pauseReplay() {
  clearInterval(replayTimer);
  replayTimer = null;
  setReplayControlsState(false);
}

// "Just show me the final state" escape hatch — skips straight to everything
// loaded at once, same as live mode's instant behavior.
function skipReplayToEnd() {
  pauseReplay();
  candleSeries.setData(replayCandles.map(toChartCandle));
  patterns = replayPatterns;
  candleSeries.setMarkers(buildSignalMarkers(replaySignals));
  replayIndex = replayCandles.length;
  redrawOverlay();
  updateReplayProgress();
}

function setReplayControlsState(playing) {
  const playBtn = document.getElementById('replay-play-btn');
  if (playBtn) playBtn.textContent = playing ? '⏸ Pause' : '▶ Play';
}

function updateReplayProgress() {
  const label = document.getElementById('replay-progress');
  if (label) label.textContent = `${replayIndex}/${replayCandles.length}`;
}

// --- orchestration ------------------------------------------------------

async function refreshAll() {
  pauseReplay();

  if (historicalRange) {
    document.getElementById('replay-controls').style.display = 'flex';
    await unsubscribeLive();
    await loadHistoricalReplayData();
    startReplay();
  } else {
    document.getElementById('replay-controls').style.display = 'none';
    await loadLiveCandles();
    await loadLivePatterns();
    await loadLiveSignals();
    await subscribeLive();
  }

  resizeOverlay();
}

// --- replay controls (play/pause, speed, skip-to-end) ------------------------

document.getElementById('replay-play-btn').addEventListener('click', () => {
  if (replayTimer) {
    pauseReplay();
  } else if (replayIndex < replayCandles.length) {
    startReplay();
  }
});

document.getElementById('replay-speed').addEventListener('change', e => {
  replaySpeedMs = Number(e.target.value);
  if (replayTimer) startReplay(); // restart interval at the new speed
});

document.getElementById('replay-skip-btn').addEventListener('click', skipReplayToEnd);

// --- date range picker (live vs. historical mode) ---------------------------

document.getElementById('load-range-btn').addEventListener('click', () => {
  const from = document.getElementById('from-date').value;
  const to = document.getElementById('to-date').value;
  if (!from || !to) return;

  historicalRange = { from, to };
  document.getElementById('live-btn').classList.remove('active');
  refreshAll();
});

document.getElementById('live-btn').addEventListener('click', () => {
  historicalRange = null;
  document.getElementById('from-date').value = '';
  document.getElementById('to-date').value = '';
  document.getElementById('live-btn').classList.add('active');
  refreshAll();
});

// --- pattern highlighting on the canvas overlay -----------------------------
// Patterns are drawn as a background band spanning every candle involved (per
// Dashboard-Frontend-Requirements.md item 6), styled distinctly from signal
// arrows (which use the chart's native marker API) so "pattern" vs "signal"
// stays visually unambiguous, not just a legend note.

function redrawOverlay() {
  overlayCtx.clearRect(0, 0, overlay.width, overlay.height);
  drawPatterns();
  drawUserShapes();
}

function drawPatterns() {
  const ts = chart.timeScale();
  for (const p of patterns) {
    const x1 = ts.timeToCoordinate(Math.floor(new Date(p.startTime).getTime() / 1000));
    const x2 = ts.timeToCoordinate(Math.floor(new Date(p.endTime).getTime() / 1000));
    if (x1 === null || x2 === null) continue;

    const left = Math.min(x1, x2) - 8;
    const width = Math.abs(x2 - x1) + 16;

    // Was 0.15 alpha with no border — technically always drawn (patterns are
    // an always-on layer regardless of which strategy fired a signal), but
    // that faint a tint on the dark background was easy to miss entirely,
    // which read as "the highlight isn't showing." Bumped to a visible fill
    // plus a solid border so a pattern band is unmistakable next to a signal
    // arrow, not just technically present.
    overlayCtx.fillStyle = 'rgba(188, 140, 255, 0.28)';
    overlayCtx.fillRect(left, 0, width, overlay.height);
    overlayCtx.strokeStyle = 'rgba(188, 140, 255, 0.8)';
    overlayCtx.lineWidth = 1;
    overlayCtx.strokeRect(left, 0, width, overlay.height);

    overlayCtx.fillStyle = '#bc8cff';
    overlayCtx.font = 'bold 11px sans-serif';
    overlayCtx.fillText(p.name, left, 14);
  }
}

// --- drawing tools (trendline / horizontal level) ---------------------------

let activeTool = 'cursor';
let shapes = []; // { type: 'trendline', p1: {time, price}, p2: {time, price} } | { type: 'horizontal', price }
let pendingPoint = null;

document.getElementById('tools-bar').addEventListener('click', e => {
  const btn = e.target.closest('.tool-btn');
  if (!btn) return;

  if (btn.dataset.tool === 'eraser') {
    shapes = [];
    redrawOverlay();
    return;
  }

  document.querySelectorAll('.tool-btn').forEach(b => b.classList.remove('active'));
  btn.classList.add('active');
  activeTool = btn.dataset.tool;
  overlay.classList.toggle('tool-active', activeTool !== 'cursor');
  pendingPoint = null;
});

overlay.addEventListener('click', e => {
  if (activeTool === 'cursor') return;
  const rect = overlay.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;
  const time = chart.timeScale().coordinateToTime(x);
  const price = candleSeries.coordinateToPrice(y);
  if (time === null || price === null) return;

  if (activeTool === 'horizontal') {
    shapes.push({ type: 'horizontal', price });
    redrawOverlay();
    return;
  }

  if (activeTool === 'trendline') {
    if (!pendingPoint) {
      pendingPoint = { time, price };
    } else {
      shapes.push({ type: 'trendline', p1: pendingPoint, p2: { time, price } });
      pendingPoint = null;
      redrawOverlay();
    }
  }
});

function drawUserShapes() {
  const ts = chart.timeScale();
  overlayCtx.strokeStyle = '#d85a30';
  overlayCtx.lineWidth = 1.5;

  for (const shape of shapes) {
    if (shape.type === 'horizontal') {
      const y = candleSeries.priceToCoordinate(shape.price);
      if (y === null) continue;
      overlayCtx.beginPath();
      overlayCtx.moveTo(0, y);
      overlayCtx.lineTo(overlay.width, y);
      overlayCtx.stroke();
    } else if (shape.type === 'trendline') {
      const x1 = ts.timeToCoordinate(shape.p1.time);
      const y1 = candleSeries.priceToCoordinate(shape.p1.price);
      const x2 = ts.timeToCoordinate(shape.p2.time);
      const y2 = candleSeries.priceToCoordinate(shape.p2.price);
      if (x1 === null || y1 === null || x2 === null || y2 === null) continue;
      overlayCtx.beginPath();
      overlayCtx.moveTo(x1, y1);
      overlayCtx.lineTo(x2, y2);
      overlayCtx.stroke();
    }
  }
}

// --- live updates via SignalR ------------------------------------------------

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/candles')
  .withAutomaticReconnect()
  .build();

connection.on('CandleUpdate', candle => {
  candleSeries.update(toChartCandle(candle));
});

let hubStarted = false;
async function subscribeLive() {
  if (!hubStarted) {
    await connection.start();
    hubStarted = true;
  }
  await connection.invoke('SubscribeToSymbol', currentSymbol, currentInterval);
}

async function unsubscribeLive() {
  if (!hubStarted) return; // nothing to unsubscribe from yet
  await connection.invoke('Unsubscribe');
}

// --- boot ---------------------------------------------------------------

loadSymbols();
refreshAll();
