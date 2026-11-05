import { useState, type FormEvent } from 'react'
import { errorMessage } from '../api/client'
import { useSubmit } from '../api/hooks'
import { Button } from '../components/Button'
import { InlineError } from '../components/DetailPane'
import { Field, Input, Textarea } from '../components/Field'
import styles from './Page.module.css'

const minLength = 10
const maxLength = 10_000

export function SubmitPage() {
  const submit = useSubmit()
  const [text, setText] = useState('')
  const [email, setEmail] = useState('')

  function onSubmit(event: FormEvent) {
    event.preventDefault()
    submit.mutate(
      { text: text.trim(), email: email.trim() || undefined },
      { onSuccess: () => { setText(''); setEmail('') } },
    )
  }

  const tooShort = text.trim().length < minLength

  return (
    <div className={styles.page}>
      <header className={styles.header}><h1>Submit feedback</h1></header>
      <form className={styles.form} onSubmit={onSubmit}>
        <Field label="What happened, or what would you like?" htmlFor="s-text">
          <Textarea id="s-text" rows={10} value={text} maxLength={maxLength} onChange={(e) => setText(e.target.value)}
            placeholder="Steps, what you expected, what you saw, and your Visual Studio version." />
        </Field>
        <Field label="Email (optional)" htmlFor="s-email">
          <Input id="s-email" type="email" value={email} maxLength={320} onChange={(e) => setEmail(e.target.value)}
            className={styles.email} />
        </Field>
        <div className={styles.actions}>
          <Button type="submit" variant="primary" disabled={tooShort || submit.isPending}>Send feedback</Button>
        </div>
        {submit.isError && <InlineError>{errorMessage(submit.error)}</InlineError>}
        {submit.isSuccess && <p>Received as #{submit.data.id}.</p>}
        <p className={styles.secondary}>
          What happens next: a reviewer reads every item, checks it against existing reports, and files it to the
          team&apos;s backlog or merges it into an earlier report.
        </p>
      </form>
    </div>
  )
}
