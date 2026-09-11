// Responsive, interactive canvas charts for the static dashboard.

const chartStates = new WeakMap();
const numberFormatter = new Intl.NumberFormat('pt-BR');

export const CHART_COLORS = ['#F5F5F5', '#C9CBD2', '#A4A6AF', '#858892', '#D9DADD', '#70737C'];
export const DONUT_COLORS = CHART_COLORS;

export function drawBarChart(canvas, series, options = {}) {
  mountChart(canvas, 'bar', series, options);
}

export function drawLineChart(canvas, series, options = {}) {
  mountChart(canvas, 'line', series, options);
}

export function drawDonutChart(canvas, series, options = {}) {
  mountChart(canvas, 'donut', series, options);
}

export function findChartHit(regions, x, y) {
  return (regions ?? []).find((region) => {
    if (region.kind === 'rect') {
      return x >= region.x && x <= region.x + region.width
        && y >= region.y && y <= region.y + region.height;
    }

    if (region.kind === 'circle') {
      return Math.hypot(x - region.x, y - region.y) <= region.radius;
    }

    if (region.kind === 'arc') {
      const distance = Math.hypot(x - region.x, y - region.y);
      if (distance < region.innerRadius || distance > region.outerRadius) return false;
      const angle = normalizeAngle(Math.atan2(y - region.y, x - region.x));
      const start = normalizeAngle(region.start);
      let end = normalizeAngle(region.end);
      if (end <= start) end += Math.PI * 2;
      const comparable = angle < start ? angle + Math.PI * 2 : angle;
      return comparable >= start && comparable <= end;
    }

    return false;
  }) ?? null;
}

export function formatChartTooltip(point, total, options = {}) {
  const value = Number(point?.value ?? point?.y) || 0;
  const parts = [String(point?.label ?? point?.x ?? '—')];
  parts.push(`${numberFormatter.format(value)} ${options.valueLabel ?? 'eventos'}`);
  if (options.showPercent !== false && total > 0) {
    parts.push(`${Math.round((value / total) * 1000) / 10}% do total exibido`);
  }
  return parts;
}

function mountChart(canvas, type, series, options) {
  if (!canvas) return;

  let state = chartStates.get(canvas);
  if (!state) {
    state = createChartState(canvas);
    chartStates.set(canvas, state);
  }

  state.type = type;
  state.series = series ?? [];
  state.options = options;
  state.hoveredIndex = state.series.length ? Math.min(state.hoveredIndex, state.series.length - 1) : -1;
  paint(canvas, state);
}

function createChartState(canvas) {
  const tooltip = document.createElement('div');
  tooltip.className = 'chart-tooltip';
  tooltip.hidden = true;
  tooltip.setAttribute('role', 'status');
  canvas.parentElement?.appendChild(tooltip);

  const state = {
    type: 'bar',
    series: [],
    options: {},
    regions: [],
    hoveredIndex: -1,
    tooltip,
  };

  canvas.addEventListener('pointermove', (event) => {
    const point = canvasPoint(canvas, event.clientX, event.clientY);
    const hit = findChartHit(state.regions, point.x, point.y);
    const nextIndex = hit?.index ?? -1;
    if (state.hoveredIndex !== nextIndex) {
      state.hoveredIndex = nextIndex;
      paint(canvas, state);
    }
    updateTooltip(state, hit, point);
  });

  canvas.addEventListener('pointerleave', () => {
    state.hoveredIndex = -1;
    state.tooltip.hidden = true;
    paint(canvas, state);
  });

  canvas.addEventListener('focus', () => {
    if (state.series.length && state.hoveredIndex < 0) state.hoveredIndex = 0;
    paint(canvas, state);
    updateTooltip(state, state.regions[state.hoveredIndex]);
  });

  canvas.addEventListener('blur', () => {
    state.hoveredIndex = -1;
    state.tooltip.hidden = true;
    paint(canvas, state);
  });

  canvas.addEventListener('keydown', (event) => {
    if (!state.series.length || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Escape'].includes(event.key)) return;
    event.preventDefault();
    if (event.key === 'Escape') {
      state.hoveredIndex = -1;
      state.tooltip.hidden = true;
    } else {
      const direction = event.key === 'ArrowRight' || event.key === 'ArrowDown' ? 1 : -1;
      state.hoveredIndex = (Math.max(0, state.hoveredIndex) + direction + state.series.length) % state.series.length;
    }
    paint(canvas, state);
    updateTooltip(state, state.regions[state.hoveredIndex]);
  });

  if (typeof ResizeObserver === 'function') {
    state.resizeObserver = new ResizeObserver(() => paint(canvas, state));
    state.resizeObserver.observe(canvas);
  } else {
    window.addEventListener('resize', () => paint(canvas, state));
  }

  return state;
}

function paint(canvas, state) {
  const { ctx, width, height } = prepareCanvas(canvas);
  ctx.clearRect(0, 0, width, height);

  if (!state.series.length) {
    state.regions = [];
    drawEmptyState(ctx, width, height);
    return;
  }

  if (state.type === 'line') {
    state.regions = paintLineChart(ctx, width, height, state);
  } else if (state.type === 'donut') {
    state.regions = paintDonutChart(ctx, width, height, state);
  } else {
    state.regions = state.options.horizontal
      ? paintHorizontalBarChart(ctx, width, height, state)
      : paintVerticalBarChart(ctx, width, height, state);
  }
}

function prepareCanvas(canvas) {
  const width = Math.max(220, Math.round(canvas.clientWidth || Number(canvas.getAttribute('width')) || 300));
  const height = Math.max(120, Math.round(canvas.clientHeight || Number(canvas.getAttribute('height')) || 180));
  const ratio = Math.min(window.devicePixelRatio || 1, 2);
  const pixelWidth = Math.round(width * ratio);
  const pixelHeight = Math.round(height * ratio);
  if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
    canvas.width = pixelWidth;
    canvas.height = pixelHeight;
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  return { ctx, width, height };
}

function paintHorizontalBarChart(ctx, width, height, state) {
  const max = Math.max(...state.series.map((point) => point.value), 1);
  const rowHeight = height / state.series.length;
  const labelWidth = Math.min(Math.max(92, width * 0.38), 230);
  const barX = labelWidth + 12;
  const valueWidth = 46;
  const availableWidth = Math.max(20, width - barX - valueWidth);

  return state.series.map((point, index) => {
    const y = index * rowHeight;
    const barHeight = Math.max(7, Math.min(15, rowHeight - 10));
    const barY = y + (rowHeight - barHeight) / 2;
    const fillWidth = Math.max(0, (point.value / max) * availableWidth);
    const active = index === state.hoveredIndex;

    ctx.fillStyle = active ? '#383A40' : '#27292E';
    roundedRect(ctx, barX, barY, availableWidth, barHeight, 3);
    ctx.fillStyle = state.options.color || CHART_COLORS[index % CHART_COLORS.length];
    roundedRect(ctx, barX, barY, fillWidth, barHeight, 3);

    ctx.font = `${active ? 650 : 520} 12px Inter, "Segoe UI", sans-serif`;
    ctx.fillStyle = active ? '#FFFFFF' : '#E4E5E9';
    ctx.textAlign = 'left';
    ctx.textBaseline = 'middle';
    ctx.fillText(fitText(ctx, String(point.label), labelWidth - 8), 0, y + rowHeight / 2);

    ctx.font = '600 11px Inter, "Segoe UI", sans-serif';
    ctx.fillStyle = '#A5A7AE';
    ctx.textAlign = 'right';
    ctx.fillText(numberFormatter.format(point.value), width, y + rowHeight / 2);

    return { kind: 'rect', index, x: 0, y, width, height: rowHeight, anchorX: barX + fillWidth, anchorY: barY, point };
  });
}

function paintVerticalBarChart(ctx, width, height, state) {
  const padding = { top: 12, right: 8, bottom: 34, left: 8 };
  const max = Math.max(...state.series.map((point) => point.value), 1);
  const chartHeight = height - padding.top - padding.bottom;
  const slotWidth = (width - padding.left - padding.right) / state.series.length;

  return state.series.map((point, index) => {
    const barWidth = Math.max(4, slotWidth - 10);
    const barHeight = Math.max(0, (point.value / max) * chartHeight);
    const x = padding.left + index * slotWidth + (slotWidth - barWidth) / 2;
    const y = padding.top + chartHeight - barHeight;
    ctx.fillStyle = state.options.color || CHART_COLORS[index % CHART_COLORS.length];
    ctx.globalAlpha = index === state.hoveredIndex ? 1 : 0.82;
    roundedRect(ctx, x, y, barWidth, barHeight, 4);
    ctx.globalAlpha = 1;
    ctx.fillStyle = '#A5A7AE';
    ctx.font = '10px Inter, "Segoe UI", sans-serif';
    ctx.textAlign = 'center';
    ctx.textBaseline = 'top';
    ctx.fillText(fitText(ctx, String(point.label), slotWidth - 4), x + barWidth / 2, height - 24);
    return { kind: 'rect', index, x: x - 5, y, width: barWidth + 10, height: height - y, anchorX: x + barWidth / 2, anchorY: y, point };
  });
}

function paintLineChart(ctx, width, height, state) {
  const padding = { top: 20, right: 14, bottom: 28, left: 34 };
  const chartWidth = width - padding.left - padding.right;
  const chartHeight = height - padding.top - padding.bottom;
  const max = Math.max(...state.series.map((point) => point.y), 1);

  ctx.strokeStyle = '#2A2C31';
  ctx.lineWidth = 1;
  ctx.fillStyle = '#989AA2';
  ctx.font = '10px Inter, "Segoe UI", sans-serif';
  ctx.textAlign = 'right';
  ctx.textBaseline = 'middle';
  for (let index = 0; index <= 4; index += 1) {
    const y = padding.top + (chartHeight / 4) * index;
    ctx.beginPath();
    ctx.moveTo(padding.left, y);
    ctx.lineTo(width - padding.right, y);
    ctx.stroke();
    ctx.fillText(numberFormatter.format(Math.round(max - (max / 4) * index)), padding.left - 7, y);
  }

  const points = state.series.map((point, index) => ({
    x: padding.left + (state.series.length > 1 ? (index / (state.series.length - 1)) * chartWidth : chartWidth / 2),
    y: padding.top + chartHeight - (point.y / max) * chartHeight,
    point,
    index,
  }));

  const color = state.options.color || CHART_COLORS[0];
  const gradient = ctx.createLinearGradient(0, padding.top, 0, padding.top + chartHeight);
  gradient.addColorStop(0, `${color}38`);
  gradient.addColorStop(1, `${color}00`);
  ctx.beginPath();
  points.forEach((point, index) => index ? ctx.lineTo(point.x, point.y) : ctx.moveTo(point.x, point.y));
  ctx.lineTo(points.at(-1).x, padding.top + chartHeight);
  ctx.lineTo(points[0].x, padding.top + chartHeight);
  ctx.closePath();
  ctx.fillStyle = gradient;
  ctx.fill();

  ctx.beginPath();
  points.forEach((point, index) => index ? ctx.lineTo(point.x, point.y) : ctx.moveTo(point.x, point.y));
  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.stroke();

  points.forEach((point) => {
    const active = point.index === state.hoveredIndex;
    ctx.beginPath();
    ctx.arc(point.x, point.y, active ? 5 : 3, 0, Math.PI * 2);
    ctx.fillStyle = active ? '#FFFFFF' : color;
    ctx.fill();
    if (active) {
      ctx.strokeStyle = color;
      ctx.lineWidth = 3;
      ctx.stroke();
    }
  });

  const labelIndexes = [...new Set([0, Math.floor((points.length - 1) / 2), points.length - 1])];
  ctx.fillStyle = '#989AA2';
  ctx.font = '10px Inter, "Segoe UI", sans-serif';
  ctx.textBaseline = 'top';
  labelIndexes.forEach((index) => {
    const point = points[index];
    ctx.textAlign = index === 0 ? 'left' : index === points.length - 1 ? 'right' : 'center';
    ctx.fillText(formatAxisLabel(point.point.x), point.x, height - 19);
  });

  return points.map((point) => ({ kind: 'circle', index: point.index, x: point.x, y: point.y, radius: 13, anchorX: point.x, anchorY: point.y, point: point.point }));
}

function paintDonutChart(ctx, width, height, state) {
  const colors = state.options.colors || DONUT_COLORS;
  const total = state.series.reduce((sum, point) => sum + point.value, 0) || 1;
  const x = width / 2;
  const y = height / 2;
  const outerRadius = Math.min(width, height) * 0.42;
  const baseInnerRadius = outerRadius * 0.66;
  let start = -Math.PI / 2;

  const regions = state.series.map((point, index) => {
    const end = start + (point.value / total) * Math.PI * 2;
    const active = index === state.hoveredIndex;
    const outer = active ? outerRadius + 4 : outerRadius;
    ctx.beginPath();
    ctx.arc(x, y, outer, start, end);
    ctx.arc(x, y, baseInnerRadius, end, start, true);
    ctx.closePath();
    ctx.fillStyle = colors[index % colors.length];
    ctx.globalAlpha = state.hoveredIndex < 0 || active ? 1 : 0.45;
    ctx.fill();
    ctx.globalAlpha = 1;
    const middle = start + (end - start) / 2;
    const region = {
      kind: 'arc', index, x, y, innerRadius: baseInnerRadius, outerRadius: outer + 5,
      start, end, anchorX: x + Math.cos(middle) * outer, anchorY: y + Math.sin(middle) * outer, point,
    };
    start = end;
    return region;
  });

  ctx.fillStyle = '#FFFFFF';
  ctx.font = '700 22px Inter, "Segoe UI", sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText(numberFormatter.format(total), x, y - 5);
  ctx.fillStyle = '#A5A7AE';
  ctx.font = '11px Inter, "Segoe UI", sans-serif';
  ctx.fillText(state.options.totalLabel || 'eventos', x, y + 15);
  return regions;
}

function updateTooltip(state, region, pointer) {
  if (!region) {
    state.tooltip.hidden = true;
    return;
  }

  const total = state.series.reduce((sum, point) => sum + Number(point.value ?? point.y ?? 0), 0);
  const lines = formatChartTooltip(region.point, total, state.options);
  state.tooltip.replaceChildren(...lines.map((line, index) => {
    const element = document.createElement(index === 0 ? 'strong' : 'span');
    element.textContent = line;
    return element;
  }));
  const x = pointer?.x ?? region.anchorX;
  const y = pointer?.y ?? region.anchorY;
  const containerWidth = state.tooltip.parentElement?.clientWidth || 320;
  const horizontalMargin = Math.min(110, containerWidth / 2);
  state.tooltip.style.left = `${Math.min(containerWidth - horizontalMargin, Math.max(horizontalMargin, x))}px`;
  state.tooltip.classList.toggle('tooltip-below', y < 80);
  state.tooltip.style.top = `${y < 80 ? y + 14 : y - 12}px`;
  state.tooltip.hidden = false;
}

function canvasPoint(canvas, clientX, clientY) {
  const rect = canvas.getBoundingClientRect();
  return { x: clientX - rect.left, y: clientY - rect.top };
}

function fitText(ctx, value, maxWidth) {
  if (ctx.measureText(value).width <= maxWidth) return value;
  let shortened = value;
  while (shortened.length > 1 && ctx.measureText(`${shortened}…`).width > maxWidth) shortened = shortened.slice(0, -1);
  return `${shortened}…`;
}

function formatAxisLabel(value) {
  const raw = String(value ?? '');
  const match = raw.match(/^\d{4}-(\d{2})-(\d{2})/);
  return match ? `${match[2]}/${match[1]}` : raw;
}

function normalizeAngle(angle) {
  const fullCircle = Math.PI * 2;
  return ((angle % fullCircle) + fullCircle) % fullCircle;
}

function roundedRect(ctx, x, y, width, height, radius) {
  if (width <= 0 || height <= 0) return;
  const safeRadius = Math.min(radius, width / 2, height / 2);
  ctx.beginPath();
  ctx.roundRect(x, y, width, height, safeRadius);
  ctx.fill();
}

function drawEmptyState(ctx, width, height) {
  ctx.fillStyle = '#A5A7AE';
  ctx.font = '12px Inter, "Segoe UI", sans-serif';
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  ctx.fillText('Sem dados no período', width / 2, height / 2);
}
