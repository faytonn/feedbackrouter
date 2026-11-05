import { useSearchParams } from 'react-router'
import { useQueue } from '../api/hooks'
import type { Area, FeedbackStatus } from '../api/types'
import { DryRunBadge } from '../components/Badge'
import { Field, Select } from '../components/Field'
import { Severity } from '../components/Severity'
import { Cell, type Column } from '../components/Table'
import { ItemList } from '../features/ItemList'
import listStyles from '../features/ListPage.module.css'
import { age, areaName, areas, openStatuses, statusName, statuses, typeName } from '../lib/format'

// Type and Duplicates are in the detail pane, so they make room for the title when it is open.
function columns(paneOpen: boolean): Column[] {
  return [
    { key: 'id', label: 'ID', width: 52 },
    { key: 'title', label: 'Title' },
    { key: 'area', label: 'Area', width: 136 },
    ...(paneOpen ? [] : [{ key: 'type', label: 'Type', width: 112 }]),
    { key: 'severity', label: 'Severity', width: 104 },
    { key: 'age', label: 'Age', width: 52, align: 'right' as const },
    ...(paneOpen ? [] : [{ key: 'dups', label: 'Duplicates', width: 84, align: 'right' as const }]),
    { key: 'status', label: 'Status', width: 132 },
  ]
}

export function QueuePage() {
  const [params, setParams] = useSearchParams()
  const statusParam = params.get('status') ?? 'open'
  const area = (params.get('area') || undefined) as Area | undefined
  const serverStatus = statusParam === 'open' || statusParam === 'all' ? undefined : (statusParam as FeedbackStatus)

  const queue = useQueue(serverStatus, area)
  const items = statusParam === 'open' ? queue.data?.filter((i) => openStatuses.includes(i.status)) : queue.data

  function setFilter(name: string, value: string, fallback: string) {
    const next = new URLSearchParams(params)
    if (value === fallback) next.delete(name)
    else next.set(name, value)
    setParams(next)
  }

  return (
    <ItemList
      title="Queue"
      basePath="/queue"
      columns={columns}
      items={items}
      isPending={queue.isPending}
      error={queue.error}
      empty="No items in this filter."
      filters={
        <>
          <Field label="Status" htmlFor="f-status" inline>
            <Select id="f-status" value={statusParam} onChange={(e) => setFilter('status', e.target.value, 'open')}>
              <option value="open">Open</option>
              <option value="all">All</option>
              {statuses.map((s) => <option key={s} value={s}>{statusName(s)}</option>)}
            </Select>
          </Field>
          <Field label="Area" htmlFor="f-area" inline>
            <Select id="f-area" value={area ?? ''} onChange={(e) => setFilter('area', e.target.value, '')}>
              <option value="">All areas</option>
              {areas.map((a) => <option key={a} value={a}>{areaName(a)}</option>)}
            </Select>
          </Field>
        </>
      }
      renderCells={(item, paneOpen) => (
        <>
          <Cell mono muted>{item.id}</Cell>
          <Cell title={item.title} muted={!item.titleIsProposed}>{item.title}</Cell>
          <Cell title={item.area ? areaName(item.area) : undefined}>{item.area ? areaName(item.area) : '-'}</Cell>
          {!paneOpen && <Cell muted={!item.type}>{item.type ? typeName(item.type) : '-'}</Cell>}
          <Cell><Severity value={item.severity} /></Cell>
          <Cell align="right" muted title={item.submittedAt}>{age(item.submittedAt)}</Cell>
          {!paneOpen && <Cell align="right" muted={item.duplicateCount === 0}>{item.duplicateCount}</Cell>}
          <Cell title={item.reviewerName ? `Being reviewed by ${item.reviewerName}` : undefined}>
            <span className={listStyles.statusCell}>
              {statusName(item.status)}
              {item.workItem?.dryRun && <DryRunBadge />}
            </span>
          </Cell>
        </>
      )}
    />
  )
}
