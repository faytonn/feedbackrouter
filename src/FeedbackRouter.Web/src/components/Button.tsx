import type { ButtonHTMLAttributes, ReactNode } from 'react'
import styles from './Button.module.css'

type Props = ButtonHTMLAttributes<HTMLButtonElement> & {
  variant?: 'primary' | 'secondary'
  icon?: ReactNode
}

export function Button({ variant = 'secondary', icon, className, children, type = 'button', ...rest }: Props) {
  return (
    <button type={type} className={[styles.button, styles[variant], className].filter(Boolean).join(' ')} {...rest}>
      {icon}
      {children}
    </button>
  )
}
