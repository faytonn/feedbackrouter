import type { ReactNode } from 'react'
import styles from './Badge.module.css'

/** A small outlined label, e.g. "Dry run" on items that were never really filed. */
export function Badge({ children, title }: { children: ReactNode; title?: string }) {
  return <span className={styles.badge} title={title}>{children}</span>
}

export function DryRunBadge() {
  return <Badge title="Filed in dry-run mode. Nothing was created in Azure DevOps.">Dry run</Badge>
}
