import { useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { api, ApiError, errorMessage } from '../services/api'
import { useQuery } from '../services/useQuery'
import { environmentLabels, formatDate, statusLabels } from '../types'
import type { AccessRequest, AuditEvent } from '../types'
import { ErrorNotice, Loading, StatusBadge } from './Feedback'

export function RequestDetail({ id, email, onBack }: { id: string; email: string; onBack: () => void }) {
  const [revision, setRevision] = useState(0)
  const [message, setMessage] = useState('')
  const [error, setError] = useState('')
  const [rejecting, setRejecting] = useState(false)
  const [pending, setPending] = useState(false)
  const inFlight = useRef(false)
  const path = '/requests/' + id
  const detail = useQuery<AccessRequest>(path, email, revision)
  const audit = useQuery<AuditEvent[]>(path + '/audit', email, revision)
  const refresh = () => setRevision(value => value + 1)

  async function decide(request: AccessRequest, action: 'approve' | 'reject', reason?: string) {
    if (inFlight.current) return
    if (action === 'reject' && !reason) {
      setError('Enter a rejection reason.')
      return
    }
    inFlight.current = true
    setPending(true)
    setError('')
    setMessage('')
    try {
      await api(path + '/' + action, email, { body: { expectedVersion: request.version, reason } })
      setRejecting(false)
      setMessage(action === 'approve' ? 'Approval recorded.' : 'Rejection recorded.')
      refresh()
    } catch (failure) {
      setError(errorMessage(failure))
      if (failure instanceof ApiError && failure.status === 409) {
        setRejecting(false)
        setError('Another action changed this request. Review the refreshed details before deciding again.')
        refresh()
      }
    } finally {
      inFlight.current = false
      setPending(false)
    }
  }

  function reject(event: FormEvent<HTMLFormElement>, request: AccessRequest) {
    event.preventDefault()
    const reason = String(new FormData(event.currentTarget).get('reason') ?? '').trim()
    void decide(request, 'reject', reason)
  }

  return <>
    <button className="secondary back" type="button" onClick={onBack}>← Back to requests</button>
    {error && <ErrorNotice message={error} />}
    {message && <p className="notice success" role="status">{message}</p>}
    {detail.state === 'loading' && <Loading />}
    {detail.state === 'error' && <ErrorNotice message={detail.message} retry={refresh} />}
    {detail.state === 'ready' && <section className="panel">
      <div className="section-heading"><h2>{detail.data.application}</h2><StatusBadge status={detail.data.status} /></div>
      <dl className="details">
        <div><dt>Requester</dt><dd>{detail.data.requester}</dd></div>
        <div><dt>Environment</dt><dd>{environmentLabels[detail.data.environment]}</dd></div>
        <div><dt>Access level</dt><dd>{detail.data.accessLevel}</dd></div>
        <div><dt>Submitted</dt><dd>{formatDate(detail.data.createdAtUtc)}</dd></div>
        <div><dt>Policy</dt><dd>{detail.data.policyVersion}</dd></div>
        <div><dt>Version</dt><dd>{detail.data.version}</dd></div>
      </dl>
      <h3>Justification</h3><p className="user-text">{detail.data.justification}</p>
      {detail.data.canDecide && <div className="actions">
        {rejecting ? <form onSubmit={e => reject(e, detail.data)}>
          <fieldset disabled={pending}>
            <label>Rejection reason<textarea name="reason" rows={3} required maxLength={1000} /></label>
            <div className="button-row">
              <button className="danger" type="submit">{pending ? 'Rejecting…' : 'Confirm rejection'}</button>
              <button type="button" className="secondary" onClick={() => { setRejecting(false); setError('') }}>Cancel</button>
            </div>
          </fieldset>
        </form> : <div className="button-row">
          <button type="button" disabled={pending} onClick={() => void decide(detail.data, 'approve')}>
            {pending ? 'Approving…' : 'Approve'}</button>
          <button type="button" className="secondary" disabled={pending} onClick={() => setRejecting(true)}>Reject</button>
        </div>}
      </div>}
    </section>}
    <section className="panel">
      <h2>Audit timeline</h2>
      {audit.state === 'loading' && <Loading />}
      {audit.state === 'error' && <ErrorNotice message={audit.message} retry={refresh} />}
      {audit.state === 'ready' && (audit.data.length === 0 ? <p className="empty">No audit events yet.</p> :
        <ol className="timeline">{audit.data.map(event => <li key={event.version}>
          <div><strong>{event.action}</strong><time dateTime={event.occurredAtUtc}>{formatDate(event.occurredAtUtc)}</time></div>
          <p>{event.actor}</p>
          <p>{event.previousStatus && statusLabels[event.previousStatus] + ' → '}{statusLabels[event.resultingStatus]}</p>
          {event.reason && <p className="user-text">{event.reason}</p>}
        </li>)}</ol>)}
    </section>
  </>
}
