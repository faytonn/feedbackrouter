import { useLayoutEffect, useRef, useState, type ReactNode } from 'react'
import { scaleBand, scaleLinear } from 'd3-scale'
import styles from './Charts.module.css'

export type Datum = { key: string; label: string; value: number }

/** Width of the element, updated on resize. */
function useWidth<T extends HTMLElement>() {
  const ref = useRef<T>(null)
  const [width, setWidth] = useState(0)
  useLayoutEffect(() => {
    const el = ref.current
    if (!el) return
    setWidth(el.getBoundingClientRect().width)
    const observer = new ResizeObserver(([entry]) => setWidth(entry?.contentRect.width ?? 0))
    observer.observe(el)
    return () => observer.disconnect()
  }, [])
  return [ref, width] as const
}

/** A rectangle with only the end away from the baseline rounded. */
function barPath(x: number, y: number, w: number, h: number, horizontal: boolean) {
  const r = Math.max(0, Math.min(4, w / 2, h / 2))
  if (horizontal) {
    // Baseline on the left, rounded on the right.
    return `M${x},${y}h${w - r}a${r},${r} 0 0 1 ${r},${r}v${h - 2 * r}a${r},${r} 0 0 1 -${r},${r}h-${w - r}z`
  }
  // Baseline at the bottom, rounded at the top.
  return `M${x},${y + h}v-${h - r}a${r},${r} 0 0 1 ${r},-${r}h${w - 2 * r}a${r},${r} 0 0 1 ${r},${r}v${h - r}z`
}

function niceMax(values: number[]) {
  const max = Math.max(0, ...values)
  return max === 0 ? 4 : scaleLinear().domain([0, max]).nice(4).domain()[1] ?? max
}

/** The same numbers as a table, for screen readers. */
function DataTable({ caption, data }: { caption: string; data: Datum[] }) {
  return (
    <table className={styles.srOnly}>
      <caption>{caption}</caption>
      <tbody>
        {data.map((d) => <tr key={d.key}><th scope="row">{d.label}</th><td>{d.value}</td></tr>)}
      </tbody>
    </table>
  )
}

function Tooltip({ x, y, children }: { x: number; y: number; children: ReactNode }) {
  return <div className={styles.tooltip} style={{ left: x, top: y }}>{children}</div>
}

/**
 * Vertical columns over a category axis. `tickEvery` thins the x labels
 * (e.g. every 7th day) so they never collide.
 */
export function ColumnChart({ data, caption, height = 180, tickEvery = 1, format = String }: {
  data: Datum[]
  caption: string
  height?: number
  tickEvery?: number
  format?: (d: Datum) => string
}) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<number | null>(null)
  const margin = { top: 8, right: 0, bottom: 22, left: 28 }
  const innerW = Math.max(0, width - margin.left - margin.right)
  const innerH = height - margin.top - margin.bottom

  const x = scaleBand<string>().domain(data.map((d) => d.key)).range([0, innerW]).paddingInner(data.length > 14 ? 0.15 : 0.3)
  const y = scaleLinear().domain([0, niceMax(data.map((d) => d.value))]).range([innerH, 0])
  const ticks = y.ticks(4).filter(Number.isInteger)
  const hovered = hover == null ? null : data[hover]

  return (
    <div ref={ref} className={styles.frame}>
      {width > 0 && (
        <svg width={width} height={height} role="img" aria-label={caption}>
          <g transform={`translate(${margin.left},${margin.top})`}>
            {ticks.map((t) => (
              <g key={t} transform={`translate(0,${y(t)})`}>
                <line x2={innerW} className={t === 0 ? styles.baseline : styles.grid} />
                <text x={-6} dy="0.32em" className={styles.axisLabel} textAnchor="end">{t}</text>
              </g>
            ))}
            {data.map((d, i) => {
              const bx = x(d.key) ?? 0
              const bh = innerH - y(d.value)
              return (
                <g key={d.key} onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}>
                  {/* Hit target: the full column height, wider than the mark. */}
                  <rect x={bx - (x.step() - x.bandwidth()) / 2} width={x.step()} height={innerH} fill="transparent" />
                  {d.value > 0 && (
                    <path d={barPath(bx, y(d.value), x.bandwidth(), bh, false)}
                      className={[styles.mark, hover != null && hover !== i && styles.dimmed].filter(Boolean).join(' ')} />
                  )}
                  {i % tickEvery === 0 && (
                    <text x={bx + x.bandwidth() / 2} y={innerH + 16} className={styles.axisLabel} textAnchor="middle">
                      {d.label}
                    </text>
                  )}
                </g>
              )
            })}
          </g>
        </svg>
      )}
      {hovered && hover != null && (
        <Tooltip x={margin.left + (x(hovered.key) ?? 0) + x.bandwidth() / 2} y={margin.top + y(hovered.value)}>
          {format(hovered)}
        </Tooltip>
      )}
      <DataTable caption={caption} data={data} />
    </div>
  )
}

/** Horizontal bars with labels on the left, sorted by the caller. */
export function BarChart({ data, caption, labelWidth = 168 }: { data: Datum[]; caption: string; labelWidth?: number }) {
  const [ref, width] = useWidth<HTMLDivElement>()
  const [hover, setHover] = useState<number | null>(null)
  const rowH = 24
  const valueW = 32
  const height = data.length * rowH
  const innerW = Math.max(0, width - labelWidth - valueW)
  const x = scaleLinear().domain([0, niceMax(data.map((d) => d.value))]).range([0, innerW])
  const barH = 12

  return (
    <div ref={ref} className={styles.frame}>
      {width > 0 && (
        <svg width={width} height={height} role="img" aria-label={caption}>
          <line x1={labelWidth} x2={labelWidth} y2={height} className={styles.baseline} />
          {data.map((d, i) => (
            <g key={d.key} transform={`translate(0,${i * rowH})`} onMouseEnter={() => setHover(i)} onMouseLeave={() => setHover(null)}>
              <rect width={width} height={rowH} fill="transparent" />
              <text x={labelWidth - 8} y={rowH / 2} dy="0.32em" textAnchor="end" className={styles.label}>{d.label}</text>
              {d.value > 0 && (
                <path d={barPath(labelWidth, (rowH - barH) / 2, x(d.value), barH, true)}
                  className={[styles.mark, hover != null && hover !== i && styles.dimmed].filter(Boolean).join(' ')} />
              )}
              <text x={labelWidth + x(d.value) + 6} y={rowH / 2} dy="0.32em" className={styles.value}>{d.value}</text>
            </g>
          ))}
        </svg>
      )}
      <DataTable caption={caption} data={data} />
    </div>
  )
}
