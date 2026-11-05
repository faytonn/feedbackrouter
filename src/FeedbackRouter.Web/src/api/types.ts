// Mirrors src/FeedbackRouter.Api/Contracts.cs. Enums arrive as their member names.

export type Area =
  | 'Editor'
  | 'Debugger'
  | 'Build'
  | 'Git'
  | 'Extensions'
  | 'LicensingAndSubscriptions'
  | 'Docs'
  | 'Other'

export type FeedbackType = 'Bug' | 'FeatureRequest' | 'Question'

export type FeedbackStatus = 'New' | 'Proposed' | 'InReview' | 'Approved' | 'Filed' | 'Merged' | 'Dismissed'

export type DecisionAction = 'Approve' | 'Merge' | 'Dismiss'

export type WorkItem = { id: number | null; url: string | null; filedAt: string; dryRun: boolean }

export type QueueItem = {
  id: number
  title: string
  titleIsProposed: boolean
  area: Area | null
  type: FeedbackType | null
  severity: number | null
  submittedAt: string
  duplicateCount: number
  status: FeedbackStatus
  triageError: string | null
  reviewerName: string | null
  workItem: WorkItem | null
}

export type Proposal = {
  area: Area
  type: FeedbackType
  severity: number
  title: string
  summary: string
  confidence: number
  proposedAt: string
  modelName: string
  promptVersion: string
}

export type Duplicate = { id: number; title: string; status: FeedbackStatus; similarity: number }

export type Decision = {
  action: DecisionAction
  reviewerName: string
  decidedAt: string
  finalArea: Area | null
  finalType: FeedbackType | null
  finalSeverity: number | null
  finalTitle: string | null
  finalSummary: string | null
  notes: string | null
}

export type Review = { reviewerName: string; lastActivityAt: string; expiresAt: string }

export type FeedbackDetail = {
  id: number
  source: 'Form' | 'Api' | 'Import'
  submittedAt: string
  rawText: string
  submitterEmail: string | null
  status: FeedbackStatus
  mergedIntoId: number | null
  triageError: string | null
  review: Review | null
  proposal: Proposal | null
  duplicates: Duplicate[]
  decision: Decision | null
  workItem: WorkItem | null
  filingTarget: { organization: string; project: string; dryRun: boolean }
}

export type DecisionRequest = {
  action: DecisionAction
  area?: Area
  type?: FeedbackType
  severity?: number
  title?: string
  summary?: string
  notes?: string
  mergeIntoId?: number
}

export type Count = { key: string; count: number }

export type Stats = {
  byArea: Count[]
  byStatus: Count[]
  perDay: { day: string; count: number }[]
  timeToTriageBuckets: Count[]
  open: number
  filedThisWeek: number
  medianTimeToTriageSeconds: number | null
  duplicateRate: number | null
  decided: number
}

export type Me = { name: string | null; signInEnabled: boolean; signInUrl: string; signOutUrl: string }
