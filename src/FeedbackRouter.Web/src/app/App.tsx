import { lazy, Suspense } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createBrowserRouter, Navigate } from 'react-router'
import { RouterProvider } from 'react-router/dom'
import { ApiError } from '../api/client'
import { FiledPage } from '../pages/FiledPage'
import { QueuePage } from '../pages/QueuePage'
import { StatsPage } from '../pages/StatsPage'
import { SubmitPage } from '../pages/SubmitPage'
import { Layout, ReviewerOnly } from './Layout'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 10_000,
      // Client errors (401, 404) will not fix themselves on retry.
      retry: (count, error) => !(error instanceof ApiError && error.status >= 400 && error.status < 500) && count < 2,
    },
  },
})

// The component sheet is a development aid and is left out of production builds.
const ComponentSheet = import.meta.env.DEV
  ? lazy(() => import('../dev/ComponentSheet').then((m) => ({ default: m.ComponentSheet })))
  : null

const router = createBrowserRouter([
  {
    path: '/',
    element: <Layout />,
    children: [
      { index: true, element: <Navigate to="/queue" replace /> },
      { path: 'queue/:id?', element: <ReviewerOnly><QueuePage /></ReviewerOnly> },
      { path: 'filed/:id?', element: <ReviewerOnly><FiledPage /></ReviewerOnly> },
      { path: 'stats', element: <ReviewerOnly><StatsPage /></ReviewerOnly> },
      { path: 'submit', element: <SubmitPage /> },
      { path: '*', element: <Navigate to="/queue" replace /> },
    ],
  },
  ...(ComponentSheet
    ? [{ path: '/dev/components', element: <Suspense><ComponentSheet /></Suspense> }]
    : []),
])

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )
}
