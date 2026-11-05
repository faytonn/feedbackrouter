import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { request } from './client'
import type { Area, DecisionRequest, FeedbackDetail, FeedbackStatus, Me, QueueItem, Stats } from './types'

export const keys = {
  me: ['me'] as const,
  queue: (status?: FeedbackStatus, area?: Area) => ['queue', status ?? null, area ?? null] as const,
  detail: (id: number) => ['feedback', id] as const,
  stats: ['stats'] as const,
}

export function useMe() {
  return useQuery({ queryKey: keys.me, queryFn: () => request<Me>('GET', '/api/me'), staleTime: Infinity })
}

export function useQueue(status: FeedbackStatus | undefined, area: Area | undefined, enabled = true) {
  const params = new URLSearchParams()
  if (status) params.set('status', status)
  if (area) params.set('area', area)
  return useQuery({
    queryKey: keys.queue(status, area),
    queryFn: () => request<QueueItem[]>('GET', `/api/queue?${params.toString()}`),
    enabled,
  })
}

export function useDetail(id: number | undefined) {
  return useQuery({
    queryKey: keys.detail(id ?? 0),
    queryFn: () => request<FeedbackDetail>('GET', `/api/feedback/${id}`),
    enabled: id !== undefined,
  })
}

export function useStats(enabled = true) {
  return useQuery({ queryKey: keys.stats, queryFn: () => request<Stats>('GET', '/api/stats'), enabled })
}

/** After any change to an item: store the fresh detail and refetch lists and stats. */
function useItemUpdated() {
  const client = useQueryClient()
  return (detail: FeedbackDetail | undefined, id: number) => {
    if (detail) {
      client.setQueryData(keys.detail(id), detail)
    } else {
      void client.invalidateQueries({ queryKey: keys.detail(id) })
    }
    void client.invalidateQueries({ queryKey: ['queue'] })
    void client.invalidateQueries({ queryKey: keys.stats })
  }
}

export function useDecision(id: number) {
  const updated = useItemUpdated()
  return useMutation({
    mutationFn: (body: DecisionRequest) => request<FeedbackDetail>('POST', `/api/feedback/${id}/decision`, body),
    onSuccess: (detail) => updated(detail, id),
    // A failed filing still changed the item (it is now Approved), so refetch it.
    onError: () => updated(undefined, id),
  })
}

export function useReview(id: number) {
  const updated = useItemUpdated()
  return useMutation({
    mutationFn: (action: 'start' | 'release') =>
      request<FeedbackDetail>(action === 'start' ? 'POST' : 'DELETE', `/api/feedback/${id}/review`),
    onSuccess: (detail) => updated(detail, id),
    onError: () => updated(undefined, id),
  })
}

export function useSubmit() {
  const client = useQueryClient()
  return useMutation({
    mutationFn: (body: { text: string; email?: string }) =>
      request<{ id: number; status: FeedbackStatus }>('POST', '/api/feedback', { ...body, source: 'form' }),
    onSuccess: () => {
      void client.invalidateQueries({ queryKey: ['queue'] })
      void client.invalidateQueries({ queryKey: keys.stats })
    },
  })
}
