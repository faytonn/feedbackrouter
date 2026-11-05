import type { ReactNode } from 'react'
import styles from './Rail.module.css'

export type RailItem = { key: string; label: string; icon: ReactNode; count?: number }

/** The left navigation. Rendering of each link is left to the caller so it can use the router. */
export function Rail({ items, renderLink }: {
  items: RailItem[]
  renderLink: (item: RailItem, className: string, activeClassName: string, content: ReactNode) => ReactNode
}) {
  return (
    <nav className={styles.rail} aria-label="Main">
      <div className={styles.product}>FeedbackRouter</div>
      <ul className={styles.list}>
        {items.map((item) => (
          <li key={item.key}>
            {renderLink(item, styles.link ?? '', styles.active ?? '', (
              <>
                {item.icon}
                <span className={styles.label}>{item.label}</span>
                {item.count != null && <span className={styles.count}>{item.count}</span>}
              </>
            ))}
          </li>
        ))}
      </ul>
    </nav>
  )
}
