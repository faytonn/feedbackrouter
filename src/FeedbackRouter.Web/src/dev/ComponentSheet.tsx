import type { ReactNode } from 'react'
import { ChartColumn, FileCheck, GitMerge, Inbox, SquarePen } from 'lucide-react'
import { Button } from '../components/Button'
import { DetailPane, InlineError, RawText, Section } from '../components/DetailPane'
import { Field, Input, Select, Textarea } from '../components/Field'
import { Rail, type RailItem } from '../components/Rail'
import { Severity } from '../components/Severity'
import { Cell, Row, SkeletonRow, Table, type Column } from '../components/Table'
import styles from './ComponentSheet.module.css'

// Static sample data for reviewing the components before any screen is built.
const columns: Column[] = [
  { key: 'id', label: 'ID', width: 48 },
  { key: 'title', label: 'Title' },
  { key: 'area', label: 'Area', width: 136 },
  { key: 'type', label: 'Type', width: 108 },
  { key: 'severity', label: 'Severity', width: 72 },
  { key: 'age', label: 'Age', width: 52, align: 'right' },
  { key: 'dups', label: 'Duplicates', width: 84, align: 'right' },
  { key: 'status', label: 'Status', width: 88 },
]

type SampleRow = {
  id: number
  title: string
  area: string
  type: string
  severity: number | null
  age: string
  dups: number
  status: string
}

const rows: SampleRow[] = [
  { id: 1, title: 'Breakpoints not hit after updating to 18.1.2', area: 'Debugger', type: 'Bug', severity: 1, age: '29d', dups: 0, status: 'Proposed' },
  { id: 24, title: 'Breakpoints in Razor Pages handlers are skipped since 18.1.2', area: 'Debugger', type: 'Bug', severity: 1, age: '12d', dups: 2, status: 'Proposed' },
  { id: 3, title: 'License goes stale daily behind a corporate proxy', area: 'Licensing & Subscriptions', type: 'Bug', severity: 2, age: '28d', dups: 0, status: 'Proposed' },
  { id: 5, title: 'Switching branches in Git Changes freezes the IDE on large repos', area: 'Git', type: 'Bug', severity: 2, age: '27d', dups: 0, status: 'Approved' },
  { id: 13, title: 'Intermittent MSB4018 in GenerateDepsFile: deps.json locked by another process', area: 'Build', type: 'Bug', severity: 3, age: '20d', dups: 0, status: 'Proposed' },
  { id: 15, title: 'Run test assemblies in parallel from Test Explorer', area: 'Editor', type: 'Question', severity: 4, age: '19d', dups: 0, status: 'Proposed' },
  { id: 40, title: 'Git Changes shows every file as modified after clone on Windows', area: 'Git', type: 'Bug', severity: null, age: '4h', dups: 0, status: 'New' },
]

const railItems: RailItem[] = [
  { key: 'queue', label: 'Queue', icon: <Inbox />, count: 31 },
  { key: 'filed', label: 'Filed', icon: <FileCheck />, count: 6 },
  { key: 'stats', label: 'Stats', icon: <ChartColumn /> },
  { key: 'submit', label: 'Submit', icon: <SquarePen /> },
]

const rawText = `Since 18.1.2 every breakpoint in my Razor Pages handlers (OnPostAsync etc.) shows as a hollow circle and is skipped. Unchecking Just My Code changes nothing. A colleague on 18.0 debugs the same repo fine.`

function QueueRows({ selectedId }: { selectedId?: number }) {
  return rows.map((r) => (
    <Row key={r.id} selected={r.id === selectedId} tabIndex={-1}>
      <Cell mono muted>{r.id}</Cell>
      <Cell title={r.title} muted={r.status === 'New'}>{r.title}</Cell>
      <Cell>{r.area}</Cell>
      <Cell>{r.type}</Cell>
      <Cell><Severity value={r.severity} /></Cell>
      <Cell align="right" muted>{r.age}</Cell>
      <Cell align="right">{r.dups > 0 ? r.dups : ''}</Cell>
      <Cell muted>{r.status}</Cell>
    </Row>
  ))
}

function Filters() {
  return (
    <div className={styles.filters}>
      <Field label="Status" htmlFor="f-status" inline>
        <Select id="f-status" defaultValue="open">
          <option value="open">Open</option>
          <option value="New">New</option>
          <option value="Proposed">Proposed</option>
          <option value="Approved">Approved</option>
        </Select>
      </Field>
      <Field label="Area" htmlFor="f-area" inline>
        <Select id="f-area" defaultValue="">
          <option value="">All areas</option>
          <option>Debugger</option>
          <option>Git</option>
        </Select>
      </Field>
      <span className={styles.count}>31 items</span>
    </div>
  )
}

function Detail({ error }: { error?: boolean }) {
  return (
    <DetailPane
      heading={<><span className={styles.mono}>#24</span><span>Proposed</span></>}
      onClose={() => {}}
      footer={
        <>
          <div className={styles.actions}>
            <Button variant="primary">Approve and file</Button>
            <Button>Dismiss</Button>
          </div>
          {error
            ? <InlineError>Azure DevOps rejected the PAT (401). Check that it is valid and has Work Items read and write scope.</InlineError>
            : <p className={styles.secondary}>Dry run: would file to FeedbackRouter</p>}
        </>
      }
    >
      <Section title="Feedback" aside="12 days ago via form, d.morales@contoso.com">
        <RawText>{rawText}</RawText>
      </Section>
      <Section title="Proposal" aside="gpt-4o-mini, triage.v1, confidence 0.82">
        <div className={styles.grid3}>
          <Field label="Area" htmlFor="p-area">
            <Select id="p-area" defaultValue="Debugger"><option>Debugger</option></Select>
          </Field>
          <Field label="Type" htmlFor="p-type">
            <Select id="p-type" defaultValue="Bug"><option>Bug</option></Select>
          </Field>
          <Field label="Severity" htmlFor="p-sev">
            <Select id="p-sev" defaultValue="1"><option value="1">1 Critical</option></Select>
          </Field>
        </div>
        <Field label="Title" htmlFor="p-title">
          <Input id="p-title" defaultValue="Breakpoints in Razor Pages handlers are skipped since 18.1.2" />
        </Field>
        <Field label="Summary" htmlFor="p-summary">
          <Textarea id="p-summary" rows={3} defaultValue="Since 18.1.2 breakpoints in Razor Pages handler methods are never hit and show as hollow circles. Disabling Just My Code does not help, and the same repo debugs normally on 18.0." />
        </Field>
      </Section>
      <Section title="Possible duplicates" aside="cosine similarity">
        <ul className={styles.dupes}>
          <li>
            <span className={styles.mono}>#1</span>
            <span className={styles.dupTitle}>Breakpoints not hit after updating to 18.1.2</span>
            <span className={styles.similarity}>0.91</span>
            <Button icon={<GitMerge />}>Merge into this</Button>
          </li>
          <li>
            <span className={styles.mono}>#10</span>
            <span className={styles.dupTitle}>Breakpoints not hitting since last update in Web API project</span>
            <span className={styles.similarity}>0.87</span>
            <Button icon={<GitMerge />}>Merge into this</Button>
          </li>
        </ul>
      </Section>
    </DetailPane>
  )
}

function Specimen({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className={styles.specimen}>
      <div className={styles.specimenLabel}>{label}</div>
      <div className={styles.specimenBody}>{children}</div>
    </div>
  )
}

function ComponentsView() {
  return (
    <div className={styles.sheet}>
      <h1>Components</h1>
      <Specimen label="Button">
        <Button variant="primary">Approve and file</Button>
        <Button>Dismiss</Button>
        <Button icon={<GitMerge />}>Merge into this</Button>
        <Button variant="primary" disabled>Approve and file</Button>
        <Button disabled>Dismiss</Button>
      </Specimen>
      <Specimen label="Select">
        <Field label="Status" htmlFor="s1" inline>
          <Select id="s1" defaultValue="open"><option value="open">Open</option></Select>
        </Field>
        <Field label="Area" htmlFor="s2">
          <Select id="s2" defaultValue="Licensing"><option value="Licensing">Licensing & Subscriptions</option></Select>
        </Field>
      </Specimen>
      <Specimen label="Input">
        <Field label="Title" htmlFor="i1">
          <Input id="i1" defaultValue="License goes stale daily behind a corporate proxy" style={{ width: 360 }} />
        </Field>
        <Field label="Email (optional)" htmlFor="i2">
          <Input id="i2" placeholder="you@example.com" style={{ width: 240 }} />
        </Field>
      </Specimen>
      <Specimen label="Severity">
        <Severity value={1} showName />
        <Severity value={2} showName />
        <Severity value={3} showName />
        <Severity value={4} showName />
        <Severity value={null} />
      </Specimen>
      <Specimen label="Table: rows, hover, selected, skeleton">
        <div className={styles.tableFrame}>
          <Table columns={columns}>
            <QueueRows selectedId={24} />
            <SkeletonRow columns={columns} />
            <SkeletonRow columns={columns} />
          </Table>
        </div>
      </Specimen>
      <Specimen label="Empty state and inline error">
        <div className={styles.stack}>
          <p className={styles.secondary}>No items in this filter.</p>
          <InlineError>Azure DevOps rejected the PAT (401). Check that it is valid and has Work Items read and write scope.</InlineError>
        </div>
      </Specimen>
    </div>
  )
}

function LayoutView({ loading, error }: { loading?: boolean; error?: boolean }) {
  return (
    <div className={styles.app}>
      <Rail
        items={railItems}
        renderLink={(item, className, active, content) => (
          <a href="#" className={[className, item.key === 'queue' && active].filter(Boolean).join(' ')}>{content}</a>
        )}
      />
      <main className={styles.main}>
        <header className={styles.mainHeader}>
          <h1>Queue</h1>
        </header>
        <Filters />
        <div className={styles.tableScroll}>
          <Table columns={columns}>
            {loading
              ? Array.from({ length: 8 }, (_, i) => <SkeletonRow key={i} columns={columns} />)
              : <QueueRows selectedId={24} />}
          </Table>
        </div>
      </main>
      {!loading && <Detail error={error} />}
    </div>
  )
}

export function ComponentSheet() {
  const view = new URLSearchParams(window.location.search).get('view')
  if (view === 'layout') return <LayoutView />
  if (view === 'loading') return <LayoutView loading />
  if (view === 'error') return <LayoutView error />
  return <ComponentsView />
}
