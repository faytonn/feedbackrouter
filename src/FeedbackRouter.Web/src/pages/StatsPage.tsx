import { errorMessage } from '../api/client'
import { useStats } from '../api/hooks'
import type { Stats } from '../api/types'
import { BarChart, ColumnChart } from '../components/charts/Charts'
import { InlineError, SkeletonLines } from '../components/DetailPane'
import { areaName, duration } from '../lib/format'
import styles from './Page.module.css'

const bucketLabels: Record<string, string> = {
  '<1h': 'Under 1h',
  '1-4h': '1 to 4h',
  '4-24h': '4 to 24h',
  '1-3d': '1 to 3d',
  '>3d': 'Over 3d',
}

function shortDay(iso: string) {
  return new Date(`${iso}T00:00:00Z`).toLocaleDateString(undefined, { month: 'short', day: 'numeric', timeZone: 'UTC' })
}

export function StatsPage() {
  const stats = useStats()

  return (
    <div className={styles.page}>
      <header className={styles.header}><h1>Stats</h1></header>
      {stats.isPending && <div className={styles.form}><SkeletonLines count={6} /></div>}
      {stats.isError && <div className={styles.form}><InlineError>{errorMessage(stats.error)}</InlineError></div>}
      {stats.data && <StatsBody stats={stats.data} />}
    </div>
  )
}

function StatsBody({ stats }: { stats: Stats }) {
  const perDay = stats.perDay.map((d) => ({ key: d.day, label: shortDay(d.day), value: d.count }))
  const byArea = stats.byArea
    .map((c) => ({ key: c.key, label: areaName(c.key), value: c.count }))
    .toSorted((a, b) => b.value - a.value)
  const buckets = stats.timeToTriageBuckets.map((c) => ({ key: c.key, label: bucketLabels[c.key] ?? c.key, value: c.count }))
  const submitted = perDay.reduce((sum, d) => sum + d.value, 0)
  const areaTotal = byArea.reduce((sum, d) => sum + d.value, 0)

  return (
    <>
      <div className={styles.numbers}>
        <StatNumber label="Open" value={String(stats.open)} note="New, proposed, in review or approved" />
        <StatNumber label="Filed this week" value={String(stats.filedThisWeek)} note="Since Monday, UTC" />
        <StatNumber label="Median time to triage"
          value={stats.medianTimeToTriageSeconds == null ? '-' : duration(stats.medianTimeToTriageSeconds)}
          note="Submitted to reviewer decision" />
        <StatNumber label="Duplicate rate"
          value={stats.duplicateRate == null ? '-' : `${Math.round(stats.duplicateRate * 100)}%`}
          note={`Merged out of ${stats.decided} decided`} />
      </div>
      <div className={styles.charts}>
        <section className={`${styles.chart} ${styles.chartWide}`}>
          <div className={styles.chartHeader}>
            <h2>Submitted per day</h2>
            <span className={styles.numberNote}>{submitted} in the last 30 days</span>
          </div>
          <ColumnChart data={perDay} caption="Feedback submitted per day, last 30 days" tickEvery={7}
            format={(d) => `${d.label}: ${d.value}`} />
        </section>
        <section className={styles.chart}>
          <div className={styles.chartHeader}>
            <h2>By area</h2>
            <span className={styles.numberNote}>Proposed or final area</span>
          </div>
          {areaTotal === 0
            ? <p className={styles.secondary}>No items have an area yet.</p>
            : <BarChart data={byArea} caption="Items by area" />}
        </section>
        <section className={styles.chart}>
          <div className={styles.chartHeader}>
            <h2>Time to triage</h2>
            <span className={styles.numberNote}>{stats.decided} decided</span>
          </div>
          {stats.decided === 0
            ? <p className={styles.secondary}>No decisions yet.</p>
            : <ColumnChart data={buckets} caption="Time from submission to decision" format={(d) => `${d.label}: ${d.value}`} />}
        </section>
      </div>
    </>
  )
}

function StatNumber({ label, value, note }: { label: string; value: string; note: string }) {
  return (
    <div className={styles.number}>
      <span className={styles.numberLabel}>{label}</span>
      <span className={styles.numberValue}>{value}</span>
      <span className={styles.numberNote}>{note}</span>
    </div>
  )
}
