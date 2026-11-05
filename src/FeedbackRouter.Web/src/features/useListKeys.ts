import { useEffect, useRef } from 'react'

type Handlers = Partial<Record<'j' | 'k' | 'enter' | 'escape' | 'a' | 'm' | 'd' | 'e', () => void>>

/**
 * Single-key shortcuts for a list page. Ignored while typing in a field, and
 * when a modifier is held so browser shortcuts keep working.
 */
export function useListKeys(handlers: Handlers) {
  const latest = useRef(handlers)
  useEffect(() => {
    latest.current = handlers
  })

  useEffect(() => {
    function onKey(event: KeyboardEvent) {
      if (event.ctrlKey || event.metaKey || event.altKey) return
      const target = event.target as HTMLElement | null
      const typing = target?.closest('input, textarea, select, [contenteditable="true"]')
      const key = event.key.toLowerCase() as keyof Handlers

      if (typing) {
        // Escape leaves the field so the next key is a shortcut again.
        if (key === 'escape') (target as HTMLElement).blur()
        return
      }

      // Enter on a focused button or link should click it, not open a row.
      if (key === 'enter' && target?.closest('button, a')) return

      const handler = latest.current[key]
      if (handler) {
        event.preventDefault()
        handler()
      }
    }

    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [])
}
