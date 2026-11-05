import { useImperativeHandle, useRef, useState, type Ref } from 'react'
import { Link } from 'react-router'
import { errorMessage } from '../api/client'
import { useDecision, useDetail, useMe, useReview } from '../api/hooks'
import type { Area, DecisionRequest, FeedbackDetail, FeedbackType } from '../api/types'
import { DryRunBadge } from '../components/Badge'
import { Button } from '../components/Button'
import { DetailPane, InlineError, RawText, Section, SkeletonLines } from '../components/DetailPane'
import { Field, Input, Select, Textarea } from '../components/Field'
import { age, areaName, areas, dateTime, severityNames, statusName, typeName, types } from '../lib/format'
import styles from './ItemDetail.module.css'

/** What the queue's keyboard shortcuts can ask the open pane to do. */
export type DetailHandle = {
  approve: () => void
  mergeIntoTop: () => void
  dismiss: () => void
  edit: () => void
}

const editableStatuses = ['New', 'Proposed', 'InReview', 'Approved']

// Using the form while holding a review keeps the claim alive; refresh it at most once a minute.
const heartbeatMs = 60_000

export function ItemDetail({ id, onClose, ref }: { id: number; onClose: () => void; ref?: Ref<DetailHandle> }) {
  const detail = useDetail(id)

  if (detail.isPending) {
    return (
      <DetailPane heading={`#${id}`} onClose={onClose}>
        <Section title="Feedback"><SkeletonLines count={4} /></Section>
        <Section title="Proposal"><SkeletonLines count={3} /></Section>
      </DetailPane>
    )
  }

  if (detail.isError) {
    return (
      <DetailPane heading={`#${id}`} onClose={onClose}>
        <Section title="Feedback"><InlineError>{errorMessage(detail.error)}</InlineError></Section>
      </DetailPane>
    )
  }

  return <DetailBody key={id} item={detail.data} onClose={onClose} ref={ref} />
}

function DetailBody({ item, onClose, ref }: { item: FeedbackDetail; onClose: () => void; ref?: Ref<DetailHandle> }) {
  const me = useMe().data?.name ?? null
  const decision = useDecision(item.id)
  const review = useReview(item.id)
  const [error, setError] = useState<string | null>(null)
  const titleRef = useRef<HTMLInputElement>(null)
  const lastHeartbeat = useRef(0)

  const initial = item.decision ?? null
  const [title, setTitle] = useState(initial?.finalTitle ?? item.proposal?.title ?? '')
  const [summary, setSummary] = useState(initial?.finalSummary ?? item.proposal?.summary ?? '')
  const [area, setArea] = useState<Area | ''>(initial?.finalArea ?? item.proposal?.area ?? '')
  const [type, setType] = useState<FeedbackType | ''>(initial?.finalType ?? item.proposal?.type ?? '')
  const [severity, setSeverity] = useState<number | ''>(initial?.finalSeverity ?? item.proposal?.severity ?? '')

  const heldByOther = item.review != null && item.review.reviewerName !== me
  const mine = item.review != null && item.review.reviewerName === me
  const editable = editableStatuses.includes(item.status) && !heldByOther
  const busy = decision.isPending || review.isPending

  function edited() {
    if (mine && Date.now() - lastHeartbeat.current > heartbeatMs) {
      lastHeartbeat.current = Date.now()
      review.mutate('start')
    }
  }

  function decide(body: DecisionRequest) {
    if (!editable || busy) return
    setError(null)
    decision.mutate(body, { onError: (e) => setError(errorMessage(e)) })
  }

  function approve() {
    decide({
      action: 'Approve',
      title: title.trim() || undefined,
      summary: summary.trim() || undefined,
      area: area || undefined,
      type: type || undefined,
      severity: severity === '' ? undefined : severity,
    })
  }

  function setReview(action: 'start' | 'release') {
    setError(null)
    lastHeartbeat.current = Date.now()
    review.mutate(action, { onError: (e) => setError(errorMessage(e)) })
  }

  useImperativeHandle(ref, () => ({
    approve,
    dismiss: () => decide({ action: 'Dismiss' }),
    mergeIntoTop: () => {
      const top = item.duplicates[0]
      if (top) {
        decide({ action: 'Merge', mergeIntoId: top.id })
      } else if (editable) {
        setError('No duplicate candidates to merge into.')
      }
    },
    edit: () => titleRef.current?.focus(),
  }))

  const proposalMeta = item.proposal
    ? `${item.proposal.modelName}, ${item.proposal.promptVersion}, ${Math.round(item.proposal.confidence * 100)}% confidence`
    : item.decision?.finalTitle
      ? `Filled in by ${item.decision.reviewerName}`
      : 'No proposal yet'

  return (
    <DetailPane
      heading={<><span>#{item.id}</span><span className={styles.status}>{statusName(item.status)}</span>{item.workItem?.dryRun && <DryRunBadge />}</>}
      meta={`${item.source === 'Import' ? 'Imported' : item.source === 'Form' ? 'Form' : 'API'}, ${age(item.submittedAt)} ago`}
      onClose={onClose}
      footer={<Footer item={item} editable={editable} busy={busy} error={error} onApprove={approve}
        onDismiss={() => decide({ action: 'Dismiss' })} onReview={setReview} mine={mine} />}
    >
      {heldByOther && item.review && (
        <p className={styles.banner}>Being reviewed by {item.review.reviewerName} since {dateTime(item.review.lastActivityAt)}.</p>
      )}

      <Section title="Feedback" aside={item.submitterEmail ?? undefined}>
        <RawText>{item.rawText}</RawText>
      </Section>

      <Section title="Proposal" aside={proposalMeta}>
        {item.triageError && <p className={styles.secondary}>Triage failed: {item.triageError}</p>}
        {!item.proposal && !item.triageError && editable && (
          <p className={styles.secondary}>Fill in every field to file it by hand.</p>
        )}
        <Field label="Title" htmlFor="d-title">
          <Input id="d-title" ref={titleRef} value={title} disabled={!editable} maxLength={200}
            onChange={(e) => { setTitle(e.target.value); edited() }} />
        </Field>
        <Field label="Summary" htmlFor="d-summary">
          <Textarea id="d-summary" value={summary} disabled={!editable} rows={4} maxLength={1000}
            onChange={(e) => { setSummary(e.target.value); edited() }} />
        </Field>
        <div className={styles.grid3}>
          <Field label="Area" htmlFor="d-area">
            <Select id="d-area" value={area} disabled={!editable}
              onChange={(e) => { setArea(e.target.value as Area | ''); edited() }}>
              <option value="">Choose</option>
              {areas.map((a) => <option key={a} value={a}>{areaName(a)}</option>)}
            </Select>
          </Field>
          <Field label="Type" htmlFor="d-type">
            <Select id="d-type" value={type} disabled={!editable}
              onChange={(e) => { setType(e.target.value as FeedbackType | ''); edited() }}>
              <option value="">Choose</option>
              {types.map((t) => <option key={t} value={t}>{typeName(t)}</option>)}
            </Select>
          </Field>
          <Field label="Severity" htmlFor="d-severity">
            <Select id="d-severity" value={severity} disabled={!editable}
              onChange={(e) => { setSeverity(e.target.value === '' ? '' : Number(e.target.value)); edited() }}>
              <option value="">Choose</option>
              {[1, 2, 3, 4].map((s) => <option key={s} value={s}>{s} {severityNames[s]}</option>)}
            </Select>
          </Field>
        </div>
      </Section>

      <Section title="Duplicates" aside="Similarity 0.80 or higher">
        {item.duplicates.length === 0
          ? <p className={styles.secondary}>No similar items found.</p>
          : (
            <ul className={styles.dupes}>
              {item.duplicates.map((d) => (
                <li key={d.id}>
                  <Link to={`/queue/${d.id}`} className={styles.dupId}>#{d.id}</Link>
                  <span className={styles.dupTitle} title={d.title}>{d.title}</span>
                  <span className={styles.similarity}>{d.similarity.toFixed(2)}</span>
                  <Button disabled={!editable || busy} onClick={() => decide({ action: 'Merge', mergeIntoId: d.id })}>
                    Merge into this
                  </Button>
                </li>
              ))}
            </ul>
          )}
      </Section>
    </DetailPane>
  )
}

function Footer({ item, editable, busy, error, mine, onApprove, onDismiss, onReview }: {
  item: FeedbackDetail
  editable: boolean
  busy: boolean
  error: string | null
  mine: boolean
  onApprove: () => void
  onDismiss: () => void
  onReview: (action: 'start' | 'release') => void
}) {
  const target = item.filingTarget
  const closing = error && <InlineError>{error}</InlineError>

  if (item.status === 'Filed' && item.workItem) {
    const w = item.workItem
    return (
      <>
        {w.dryRun || w.id == null
          ? <p className={styles.secondary}>Dry run: nothing was created in Azure DevOps. Recorded {dateTime(w.filedAt)}.</p>
          : <p>Filed as <a href={w.url ?? undefined} target="_blank" rel="noreferrer">work item {w.id}</a> on {dateTime(w.filedAt)}.</p>}
        {item.decision && <p className={styles.secondary}>Approved by {item.decision.reviewerName}.</p>}
      </>
    )
  }

  if (item.status === 'Merged') {
    return <p>Merged into <Link to={`/queue/${item.mergedIntoId}`}>#{item.mergedIntoId}</Link> by {item.decision?.reviewerName}.</p>
  }

  if (item.status === 'Dismissed') {
    return <p className={styles.secondary}>Dismissed by {item.decision?.reviewerName} on {item.decision && dateTime(item.decision.decidedAt)}.</p>
  }

  return (
    <>
      <div className={styles.actions}>
        <Button variant="primary" disabled={!editable || busy} onClick={onApprove}>Approve and file</Button>
        {item.status !== 'Approved' && (mine
          ? <Button disabled={busy} onClick={() => onReview('release')}>Release</Button>
          : <Button disabled={!editable || busy} onClick={() => onReview('start')}>Start review</Button>)}
        {item.status !== 'Approved' && <Button disabled={!editable || busy} onClick={onDismiss}>Dismiss</Button>}
      </div>
      {closing || (
        <p className={styles.secondary}>
          {item.status === 'Approved'
            ? 'Approved but not filed yet. Approve again to retry.'
            : target.dryRun
              ? `Dry run: would file to ${target.project}`
              : `Files to ${target.organization}/${target.project}`}
        </p>
      )}
    </>
  )
}
