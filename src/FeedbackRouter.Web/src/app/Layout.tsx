import type { ReactNode } from 'react'
import { ChartColumn, FileCheck, Inbox, SquarePen } from 'lucide-react'
import { NavLink, Outlet } from 'react-router'
import { useMe, useStats } from '../api/hooks'
import { Rail, type RailItem } from '../components/Rail'
import styles from './Layout.module.css'

export function Layout() {
  const me = useMe()
  const signedIn = me.data?.name != null
  const stats = useStats(signedIn)
  const filed = stats.data?.byStatus.find((s) => s.key === 'Filed')?.count

  const items: RailItem[] = [
    { key: 'queue', label: 'Queue', icon: <Inbox />, count: stats.data?.open },
    { key: 'filed', label: 'Filed', icon: <FileCheck />, count: filed },
    { key: 'stats', label: 'Stats', icon: <ChartColumn /> },
    { key: 'submit', label: 'Submit', icon: <SquarePen /> },
  ]

  return (
    <div className={styles.app}>
      <div className={styles.rail}>
        <Rail
          items={items}
          renderLink={(item, className, activeClassName, content) => (
            <NavLink to={`/${item.key}`} className={({ isActive }) => (isActive ? `${className} ${activeClassName}` : className)}>
              {content}
            </NavLink>
          )}
        />
        {me.data?.name && (
          <div className={styles.account}>
            <span className={styles.name} title={me.data.name}>{me.data.name}</span>
            {me.data.signInEnabled && <a href={me.data.signOutUrl}>Sign out</a>}
          </div>
        )}
      </div>
      <main className={styles.main}>
        <Outlet />
      </main>
    </div>
  )
}

/** Wraps reviewer-only pages: shows a sign-in line instead when nobody is signed in. */
export function ReviewerOnly({ children }: { children: ReactNode }) {
  const me = useMe()
  if (me.isPending) {
    return null
  }

  if (me.isError) {
    return <p className={styles.notice}>Could not reach the server.</p>
  }

  if (me.data.name == null) {
    return (
      <p className={styles.notice}>
        <a href={me.data.signInUrl}>Sign in</a> with a Microsoft account to review feedback.
      </p>
    )
  }

  return children
}
