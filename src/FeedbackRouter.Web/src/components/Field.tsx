import type { ComponentProps, ReactNode } from 'react'
import styles from './Field.module.css'

export function Field({ label, htmlFor, children, inline }: {
  label: string
  htmlFor: string
  children: ReactNode
  inline?: boolean
}) {
  return (
    <div className={inline ? styles.inlineField : styles.field}>
      <label className={styles.label} htmlFor={htmlFor}>{label}</label>
      {children}
    </div>
  )
}

export function Select({ className, ...rest }: ComponentProps<'select'>) {
  return <select className={[styles.control, styles.select, className].filter(Boolean).join(' ')} {...rest} />
}

export function Input({ className, ...rest }: ComponentProps<'input'>) {
  return <input className={[styles.control, className].filter(Boolean).join(' ')} {...rest} />
}

export function Textarea({ className, ...rest }: ComponentProps<'textarea'>) {
  return <textarea className={[styles.control, styles.textarea, className].filter(Boolean).join(' ')} {...rest} />
}
