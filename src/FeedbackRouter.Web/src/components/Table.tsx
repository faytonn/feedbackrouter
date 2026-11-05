import type { HTMLAttributes, ReactNode, TdHTMLAttributes } from 'react'
import styles from './Table.module.css'

export type Column = { key: string; label: string; width?: number; align?: 'left' | 'right' }

export function Table({ columns, children }: { columns: Column[]; children: ReactNode }) {
  return (
    <table className={styles.table}>
      <colgroup>
        {columns.map((c) => <col key={c.key} style={c.width ? { width: c.width } : undefined} />)}
      </colgroup>
      <thead>
        <tr>
          {columns.map((c) => (
            <th key={c.key} scope="col" className={c.align === 'right' ? styles.right : undefined}>{c.label}</th>
          ))}
        </tr>
      </thead>
      <tbody>{children}</tbody>
    </table>
  )
}

export function Row({ selected, className, ...rest }: HTMLAttributes<HTMLTableRowElement> & { selected?: boolean }) {
  return (
    <tr
      className={[styles.row, selected && styles.selected, className].filter(Boolean).join(' ')}
      aria-selected={selected}
      {...rest}
    />
  )
}

export function Cell({ align, muted, mono, className, ...rest }: TdHTMLAttributes<HTMLTableCellElement> & {
  align?: 'left' | 'right'
  muted?: boolean
  mono?: boolean
}) {
  return (
    <td
      className={[
        align === 'right' && styles.right,
        muted && styles.muted,
        mono && styles.mono,
        className,
      ].filter(Boolean).join(' ')}
      {...rest}
    />
  )
}

/** A placeholder row with the same height and columns as a real one. */
export function SkeletonRow({ columns }: { columns: Column[] }) {
  return (
    <tr className={styles.skeletonRow} aria-hidden>
      {columns.map((c, i) => (
        <td key={c.key} className={c.align === 'right' ? styles.right : undefined}>
          <span className={styles.bar} style={{ width: i === 1 ? `${55 + ((c.key.length * 7) % 30)}%` : '60%' }} />
        </td>
      ))}
    </tr>
  )
}
