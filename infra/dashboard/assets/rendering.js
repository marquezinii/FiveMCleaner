// Minimal, dependency-free canvas chart rendering. Deliberately simple
// (no external charting library) so the dashboard stays a handful of plain
// files Cloudflare Pages can serve statically with no build step. Touches
// the DOM/Canvas API directly, so unlike charts.js this file is not covered
// by an automated test (no headless-canvas dependency was introduced for
// that) -- verify visually once deployed.

export function drawBarChart(canvas, series, options = {}) {
  const ctx = canvas.getContext('2d');
  const { width, height } = canvas;
  ctx.clearRect(0, 0, width, height);
  if (!series || series.length === 0) {
    drawEmptyState(ctx, width, height);
    return;
  }

  const color = options.color || '#37c889';
  const max = Math.max(...series.map((point) => point.value), 1);
  const barWidth = width / series.length;

  series.forEach((point, index) => {
    const barHeight = Math.max(0, (point.value / max) * (height - 28));
    ctx.fillStyle = color;
    ctx.fillRect(index * barWidth + 4, height - barHeight - 20, Math.max(1, barWidth - 8), barHeight);

    ctx.fillStyle = '#9aa4b2';
    ctx.font = '10px "Segoe UI", sans-serif';
    ctx.textAlign = 'center';
    ctx.fillText(truncate(point.label, 12), index * barWidth + barWidth / 2, height - 6);
  });
}

export function drawLineChart(canvas, series, options = {}) {
  const ctx = canvas.getContext('2d');
  const { width, height } = canvas;
  ctx.clearRect(0, 0, width, height);
  if (!series || series.length === 0) {
    drawEmptyState(ctx, width, height);
    return;
  }

  const color = options.color || '#ff7a18';
  const max = Math.max(...series.map((point) => point.y), 1);
  const stepX = series.length > 1 ? width / (series.length - 1) : width;

  ctx.strokeStyle = color;
  ctx.lineWidth = 2;
  ctx.beginPath();
  series.forEach((point, index) => {
    const x = index * stepX;
    const y = height - (point.y / max) * (height - 20) - 10;
    if (index === 0) {
      ctx.moveTo(x, y);
    } else {
      ctx.lineTo(x, y);
    }
  });
  ctx.stroke();
}

function drawEmptyState(ctx, width, height) {
  ctx.fillStyle = '#5b6472';
  ctx.font = '12px "Segoe UI", sans-serif';
  ctx.textAlign = 'center';
  ctx.fillText('Sem dados ainda', width / 2, height / 2);
}

function truncate(text, maxLength) {
  return text.length > maxLength ? `${text.slice(0, maxLength - 1)}…` : text;
}
