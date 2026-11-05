import styles from './Severity.module.css'

const names: Record<number, string> = { 1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low' }

export function Severity({ value, showName }: { value: number | null; showName?: boolean }) {
  if (value == null) {
    return <span className={styles.none}>-</span>
  }

  return (
    <span className={styles.severity} title={`Severity ${value}: ${names[value]}`}>
      <span className={styles.swatch} style={{ background: `var(--sev-${value})` }} />
      {value}
      {showName && <span className={styles.name}>{names[value]}</span>}
    </span>
  )
}
