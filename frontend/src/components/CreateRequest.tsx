import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { api, errorMessage } from '../services/api'
import { useQuery } from '../services/useQuery'
import type { AccessLevel, AccessRequest, Application, Environment } from '../types'
import { ErrorNotice, Loading } from './Feedback'

export function CreateRequest({ email, onCreated }: { email: string; onCreated: (id: string) => void }) {
  const [revision, setRevision] = useState(0)
  const apps = useQuery<Application[]>('/applications', email, revision)
  const [environment, setEnvironment] = useState<Environment>('NonProduction')
  const [access, setAccess] = useState<AccessLevel>('Read')
  const [error, setError] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const attempt = useRef<{ payload: string; key: string } | null>(null)
  const inFlight = useRef(false)

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (inFlight.current) return
    const values = new FormData(event.currentTarget)
    const justification = String(values.get('justification') ?? '').trim()
    const applicationId = Number(values.get('application'))
    if (!applicationId || !justification || justification.length > 2000) {
      setError('Select an application and enter a justification of 1–2000 characters.')
      return
    }
    const body = { applicationId, environment, accessLevel: access, justification }
    const payload = JSON.stringify(body)
    if (attempt.current?.payload !== payload) attempt.current = { payload, key: crypto.randomUUID() }

    inFlight.current = true
    setSubmitting(true)
    setError('')
    try {
      const created = await api<AccessRequest>('/requests', email, { body: { ...body, clientRequestId: attempt.current.key } })
      onCreated(created.id)
    } catch (failure) {
      setError(errorMessage(failure))
    } finally {
      inFlight.current = false
      setSubmitting(false)
    }
  }

  return <section className="panel form-panel">
    <h2>New access request</h2>
    <p className="muted">Your manager reviews the request first.</p>
    {apps.state === 'loading' && <Loading />}
    {apps.state === 'error' && <ErrorNotice message={apps.message} retry={() => setRevision(x => x + 1)} />}
    {apps.state === 'ready' && (apps.data.length === 0 ? <p className="empty">No applications are available.</p> :
      <form onSubmit={submit}>
        <fieldset disabled={submitting}>
          <div className="field"><label htmlFor="request-application">Application</label>
            <select id="request-application" name="application" required defaultValue="">
            <option value="" disabled>Select an application</option>
            {apps.data.map(app => <option key={app.id} value={app.id}>{app.name}</option>)}
          </select></div>
          <div className="form-row">
            <div className="field"><label htmlFor="request-environment">Environment</label>
              <select id="request-environment" value={environment} onChange={e => setEnvironment(e.target.value as Environment)}>
              <option value="NonProduction">Non-production</option><option value="Production">Production</option>
            </select></div>
            <div className="field"><label htmlFor="request-access">Access level</label>
              <select id="request-access" value={access} onChange={e => setAccess(e.target.value as AccessLevel)}>
              <option value="Read">Read</option><option value="Admin">Admin</option>
            </select></div>
          </div>
          {(environment === 'Production' || access === 'Admin') &&
            <p className="notice">This request also requires system owner approval.</p>}
          <label>Justification<textarea name="justification" rows={4} required maxLength={2000}
            placeholder="Explain why you need this access." /></label>
          {error && <ErrorNotice message={error} />}
          <button type="submit">{submitting ? 'Submitting…' : 'Submit request'}</button>
        </fieldset>
      </form>)}
  </section>
}
