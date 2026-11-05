import type { ReactNode } from 'react'
import { X } from 'lucide-react'
import styles from './DetailPane.module.css'

export function DetailPane({ heading, meta, onClose, footer, children }: {
  heading: ReactNode
  meta?: ReactNode
  onClose: () => void
  footer?: ReactNode
  children: ReactNode
}) {
  return (
    <aside className={styles.pane} aria-label="Item detail">
      <header className={styles.header}>
        <div className={styles.heading}>{heading}</div>
        {meta && <div className={styles.meta}>{meta}</div>}
        <button type="button" className={styles.close} onClick={onClose} aria-label="Close detail pane">
          <X />
        </button>
      </header>
      <div className={styles.body}>{children}</div>
      {footer && <footer className={styles.footer}>{footer}</footer>}
    </aside>
  )
}

export function Section({ title, aside, children }: { title: string; aside?: ReactNode; children: ReactNode }) {
  return (
    <section className={styles.section}>
      <div className={styles.sectionHeader}>
        <h2>{title}</h2>
        {aside && <span className={styles.meta}>{aside}</span>}
      </div>
      {children}
    </section>
  )
}

export function RawText({ children }: { children: string }) {
  return <pre className={styles.raw}>{children}</pre>
}

export function InlineError({ children }: { children: ReactNode }) {
  return <p className={styles.error} role="alert">{children}</p>
}

/** Placeholder lines while the pane loads. */
export function SkeletonLines({ count }: { count: number }) {
  return (
    <div className={styles.skeleton} aria-hidden>
      {Array.from({ length: count }, (_, i) => (
        <span key={i} className={styles.skeletonLine} style={{ width: `${90 - ((i * 17) % 40)}%` }} />
      ))}
    </div>
  )
}
