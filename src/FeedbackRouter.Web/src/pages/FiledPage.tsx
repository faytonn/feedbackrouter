import { useQueue } from '../api/hooks'
import { DryRunBadge } from '../components/Badge'
import { Severity } from '../components/Severity'
import { Cell, type Column } from '../components/Table'
import { ItemList } from '../features/ItemList'
import { age, areaName } from '../lib/format'

function columns(paneOpen: boolean): Column[] {
  return [
    { key: 'id', label: 'ID', width: 52 },
    { key: 'title', label: 'Title' },
    ...(paneOpen ? [] : [{ key: 'area', label: 'Area', width: 136 }]),
    { key: 'severity', label: 'Severity', width: 104 },
    { key: 'workItem', label: 'Work item', width: 104 },
    { key: 'filed', label: 'Filed', width: 60, align: 'right' as const },
  ]
}

export function FiledPage() {
  const filed = useQueue('Filed', undefined)
  // Newest filing first.
  const items = filed.data?.toSorted((a, b) => Date.parse(b.workItem?.filedAt ?? '') - Date.parse(a.workItem?.filedAt ?? ''))

  return (
    <ItemList
      title="Filed"
      basePath="/filed"
      columns={columns}
      items={items}
      isPending={filed.isPending}
      error={filed.error}
      empty="Nothing has been filed yet."
      renderCells={(item, paneOpen) => (
        <>
          <Cell mono muted>{item.id}</Cell>
          <Cell title={item.title}>{item.title}</Cell>
          {!paneOpen && <Cell title={item.area ? areaName(item.area) : undefined}>{item.area ? areaName(item.area) : '-'}</Cell>}
          <Cell><Severity value={item.severity} /></Cell>
          <Cell>
            {item.workItem?.dryRun || item.workItem?.id == null
              ? <DryRunBadge />
              : <a href={item.workItem.url ?? undefined} target="_blank" rel="noreferrer" onClick={(e) => e.stopPropagation()}>
                  {item.workItem.id}
                </a>}
          </Cell>
          <Cell align="right" muted>{item.workItem ? age(item.workItem.filedAt) : '-'}</Cell>
        </>
      )}
    />
  )
}
