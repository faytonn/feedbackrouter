import type { Area, FeedbackStatus, FeedbackType } from '../api/types'

export const areas: Area[] = [
  'Editor', 'Debugger', 'Build', 'Git', 'Extensions', 'LicensingAndSubscriptions', 'Docs', 'Other',
]

export const types: FeedbackType[] = ['Bug', 'FeatureRequest', 'Question']

export const statuses: FeedbackStatus[] = ['New', 'Proposed', 'InReview', 'Approved', 'Filed', 'Merged', 'Dismissed']

/** What the queue shows by default: everything a reviewer still has to act on. */
export const openStatuses: FeedbackStatus[] = ['New', 'Proposed', 'InReview', 'Approved']

const areaNames: Record<Area, string> = {
  Editor: 'Editor',
  Debugger: 'Debugger',
  Build: 'Build',
  Git: 'Git',
  Extensions: 'Extensions',
  LicensingAndSubscriptions: 'Licensing & Subscriptions',
  Docs: 'Docs',
  Other: 'Other',
}

const typeNames: Record<FeedbackType, string> = { Bug: 'Bug', FeatureRequest: 'Feature request', Question: 'Question' }

const statusNames: Record<FeedbackStatus, string> = {
  New: 'New',
  Proposed: 'Proposed',
  InReview: 'In review',
  Approved: 'Approved',
  Filed: 'Filed',
  Merged: 'Merged',
  Dismissed: 'Dismissed',
}

export const severityNames: Record<number, string> = { 1: 'Critical', 2: 'High', 3: 'Medium', 4: 'Low' }

export const areaName = (a: string) => areaNames[a as Area] ?? a
export const typeName = (t: FeedbackType) => typeNames[t]
export const statusName = (s: string) => statusNames[s as FeedbackStatus] ?? s

/** Compact age: 12m, 4h, 3d. */
export function age(iso: string, now = Date.now()): string {
  const minutes = Math.max(0, Math.floor((now - Date.parse(iso)) / 60_000))
  if (minutes < 60) return `${minutes}m`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h`
  return `${Math.floor(hours / 24)}d`
}

/** A duration in seconds as 45m, 3.2h or 2.1d. */
export function duration(seconds: number): string {
  const minutes = seconds / 60
  if (minutes < 60) return `${Math.round(minutes)}m`
  const hours = minutes / 60
  if (hours < 24) return `${hours.toFixed(1)}h`
  return `${(hours / 24).toFixed(1)}d`
}

export function dateTime(iso: string): string {
  return new Date(iso).toLocaleString(undefined, { dateStyle: 'medium', timeStyle: 'short' })
}
