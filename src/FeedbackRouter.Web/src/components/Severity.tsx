import { severityNames } from '../lib/format'
import styles from './Severity.module.css'

/** A muted swatch, always next to the number and name so color is never the only signal. */
export function Severity({ value }: { value: number | null }) {
  if (value == null) {
    return <span className={styles.none}>-</span>
  }

  return (
    <span className={styles.severity}>
      <span className={styles.swatch} style={{ background: `var(--sev-${value})` }} aria-hidden />
      {value} {severityNames[value]}
    </span>
  )
}
