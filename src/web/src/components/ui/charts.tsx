import { barHeights, barWidths, type ChartPoint } from '../../lib/charts';

/**
 * Vertical columns for a short series (for example the last seven days). The page is right-to-left, so the oldest
 * point is drawn on the right and time reads toward the left. Colours and sizes come from the stylesheet.
 */
export function ColumnChart({ points, label, height = 120 }: { points: ChartPoint[]; label: string; height?: number }) {
  const slot = 56;
  const width = slot * points.length;
  const heights = barHeights(points.map((p) => p.value), height);
  return (
    <div className="chart" role="img" aria-label={label}>
      <svg viewBox={`0 0 ${width} ${height + 38}`} preserveAspectRatio="xMidYMid meet">
        <line className="axis" x1="0" x2={width} y1={height} y2={height} />
        {points.map((p, i) => {
          const x = (points.length - 1 - i) * slot;
          const h = heights[i] ?? 0;
          return (
            <g key={p.label}>
              <rect className={i === points.length - 1 ? 'bar' : 'bar alt'} x={x + 12} y={height - h} width={slot - 24} height={h} rx="4" />
              <text className="value" x={x + slot / 2} y={height - h - 6} textAnchor="middle">
                {p.text ?? p.value}
              </text>
              <text x={x + slot / 2} y={height + 16} textAnchor="middle">
                {p.label}
              </text>
            </g>
          );
        })}
      </svg>
    </div>
  );
}

/** Labelled horizontal bars; the longest bar is the largest value. */
export function BarList({ items, label }: { items: ChartPoint[]; label: string }) {
  const widths = barWidths(items.map((i) => i.value));
  return (
    <ul className="bar-list" aria-label={label}>
      {items.map((item, i) => (
        <li key={item.label} className="bar-row">
          <span>{item.label}</span>
          <svg className="bar-track" viewBox="0 0 100 6" preserveAspectRatio="none" aria-hidden="true">
            <rect x={100 - (widths[i] ?? 0)} y="0" width={widths[i] ?? 0} height="6" rx="3" className="bar" />
          </svg>
          <span className="bar-value">{item.text ?? item.value}</span>
        </li>
      ))}
    </ul>
  );
}
