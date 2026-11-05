/** An HTTP error with the server's one-line explanation (ProblemDetails.detail). */
export class ApiError extends Error {
  readonly status: number

  constructor(status: number, message: string) {
    super(message)
    this.status = status
  }
}

type Problem = { title?: string; detail?: string; errors?: Record<string, string[]> }

async function toError(response: Response): Promise<ApiError> {
  let message = `Request failed with status ${response.status}.`
  try {
    const problem = (await response.json()) as Problem
    const firstFieldError = problem.errors ? Object.values(problem.errors)[0]?.[0] : undefined
    message = firstFieldError ?? problem.detail ?? problem.title ?? message
  } catch {
    // Not JSON; keep the generic message.
  }
  return new ApiError(response.status, message)
}

export async function request<T>(method: string, url: string, body?: unknown): Promise<T> {
  let response: Response
  try {
    response = await fetch(url, {
      method,
      headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
    })
  } catch {
    throw new ApiError(0, 'Could not reach the server.')
  }

  if (!response.ok) {
    throw await toError(response)
  }
  return (await response.json()) as T
}

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : 'Something went wrong.'
}
