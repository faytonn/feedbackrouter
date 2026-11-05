import { useEffect, useRef, useState, type ReactNode } from 'react'
import { useLocation, useNavigate, useParams } from 'react-router'
import { errorMessage } from '../api/client'
import type { QueueItem } from '../api/types'
import { InlineError } from '../components/DetailPane'
import { Row, SkeletonRow, Table, type Column } from '../components/Table'
import { ItemDetail, type DetailHandle } from './ItemDetail'
import styles from './ListPage.module.css'
import { useListKeys } from './useListKeys'

/**
 * A list of items with a detail pane, shared by Queue and Filed.
 * The open item lives in the URL (/queue/24) so it survives a reload.
 */
export function ItemList({ title, basePath, filters, columns, items, isPending, error, empty, renderCells }: {
  title: string
  basePath: string
  filters?: ReactNode
  columns: (paneOpen: boolean) => Column[]
  items: QueueItem[] | undefined
  isPending: boolean
  error: unknown
  empty: string
  renderCells: (item: QueueItem, paneOpen: boolean) => ReactNode
}) {
  const navigate = useNavigate()
  const { search } = useLocation()
  const params = useParams()
  const openId = params.id ? Number(params.id) : undefined
  const [selectedId, setSelectedId] = useState<number | undefined>(openId)
  const detail = useRef<DetailHandle>(null)
  const rowRefs = useRef(new Map<number, HTMLTableRowElement>())

  const paneOpen = openId !== undefined
  const cols = columns(paneOpen)
  const list = items ?? []
  const current = selectedId ?? openId ?? list[0]?.id

  const open = (id: number) => {
    setSelectedId(id)
    void navigate(`${basePath}/${id}${search}`)
  }
  const close = () => void navigate(`${basePath}${search}`)

  function move(step: number) {
    if (list.length === 0) return
    const index = list.findIndex((i) => i.id === current)
    const next = list[Math.min(list.length - 1, Math.max(0, index === -1 ? 0 : index + step))]
    if (!next) return
    if (paneOpen) {
      open(next.id)
    } else {
      setSelectedId(next.id)
    }
  }

  useEffect(() => {
    if (current !== undefined) {
      rowRefs.current.get(current)?.scrollIntoView({ block: 'nearest' })
    }
  }, [current])

  useListKeys({
    j: () => move(1),
    k: () => move(-1),
    enter: () => current !== undefined && open(current),
    escape: () => paneOpen && close(),
    a: () => detail.current?.approve(),
    m: () => detail.current?.mergeIntoTop(),
    d: () => detail.current?.dismiss(),
    e: () => detail.current?.edit(),
  })

  let body: ReactNode
  if (isPending) {
    body = <Table columns={cols}>{Array.from({ length: 10 }, (_, i) => <SkeletonRow key={i} columns={cols} />)}</Table>
  } else if (error) {
    body = <div className={styles.messageError}><InlineError>{errorMessage(error)}</InlineError></div>
  } else if (list.length === 0) {
    body = <p className={styles.message}>{empty}</p>
  } else {
    body = (
      <Table columns={cols}>
        {list.map((item) => (
          <Row
            key={item.id}
            ref={(el: HTMLTableRowElement | null) => {
              if (el) rowRefs.current.set(item.id, el)
              else rowRefs.current.delete(item.id)
            }}
            selected={item.id === current}
            onClick={() => open(item.id)}
          >
            {renderCells(item, paneOpen)}
          </Row>
        ))}
      </Table>
    )
  }

  return (
    <div className={styles.page}>
      <section className={styles.list} aria-label={title}>
        <header className={styles.header}>
          <h1>{title}</h1>
          {items && <span className={styles.count}>{items.length} {items.length === 1 ? 'item' : 'items'}</span>}
        </header>
        {filters && <div className={styles.filters}>{filters}</div>}
        <div className={styles.scroll}>{body}</div>
        <p className={styles.keys}>
          <kbd>j</kbd> <kbd>k</kbd> move, <kbd>enter</kbd> open, <kbd>e</kbd> edit, <kbd>a</kbd> approve,{' '}
          <kbd>m</kbd> merge into top duplicate, <kbd>d</kbd> dismiss, <kbd>esc</kbd> close
        </p>
      </section>
      {openId !== undefined && <ItemDetail key={openId} id={openId} onClose={close} ref={detail} />}
    </div>
  )
}
