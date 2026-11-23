import { useState } from 'react'
import { createRoot } from 'react-dom/client'
import { CreateRequest } from './components/CreateRequest'
import { ErrorNotice, Loading, StatusBadge } from './components/Feedback'
import { RequestDetail } from './components/RequestDetail'
import { useQuery } from './services/useQuery'
import { environmentLabels, formatDate, roleLabels } from './types'
import type { AccessRequest, DemoUser } from './types'
import './styles.css'

type Page = 'mine' | 'inbox' | 'all' | 'create'

function App() {
  const [email, setEmail] = useState('')
  const [revision, setRevision] = useState(0)
  const users = useQuery<DemoUser[]>('/demo/users', undefined, revision)
  const current = users.state === 'ready' ? users.data.find(user => user.email === email) ?? users.data[0] : undefined
  return <>
    <header><div className="header-content">
      <div className="brand"><span className="brand-mark" aria-hidden="true">↳</span><h1>Access Request Hub</h1></div>
      {users.state === 'ready' && current && <label className="identity">Demo user
        <select value={current.email} onChange={event => setEmail(event.target.value)}>
          {users.data.map(user => <option key={user.email} value={user.email}>
            {user.email} · {roleLabels[user.role]}
          </option>)}
        </select>
      </label>}
    </div></header>
    <main>
      {users.state === 'loading' && <Loading />}
      {users.state === 'error' && <ErrorNotice message={users.message} retry={() => setRevision(x => x + 1)} />}
      {users.state === 'ready' && !current && <p className="empty">No demo users are available.</p>}
      {current && <Workspace key={current.email} user={current} />}
    </main>
  </>
}

function Workspace({ user }: { user: DemoUser }) {
  const [page, setPage] = useState<Page>(user.role === 'Auditor' ? 'all' : user.role === 'Requester' ? 'mine' : 'inbox')
  const [selected, setSelected] = useState<string>()
  const [submitted, setSubmitted] = useState(false)
  const tabs: { page: Page; label: string }[] = [{ page: 'mine', label: 'My requests' }]
  if (user.role === 'Manager' || user.role === 'SystemOwner') tabs.push({ page: 'inbox', label: 'Approval inbox' })
  if (user.role === 'Auditor') tabs.push({ page: 'all', label: 'All requests' })

  function navigate(target: Page) {
    setPage(target)
    setSelected(undefined)
    setSubmitted(false)
  }

  return <>
    <nav aria-label="Workspace" className="navigation">
      <div>{tabs.map(tab => <button key={tab.page} type="button" className={page === tab.page ? 'active' : ''}
        aria-current={page === tab.page ? 'page' : undefined} onClick={() => navigate(tab.page)}>{tab.label}</button>)}</div>
      {user.role === 'Requester' && page !== 'create' &&
        <button type="button" onClick={() => navigate('create')}>New request</button>}
    </nav>
    {submitted && <p className="notice success" role="status">Request submitted for review.</p>}
    {selected ? <RequestDetail key={selected} id={selected} email={user.email} onBack={() => navigate(page)} />
      : page === 'create' ? <CreateRequest email={user.email} onCreated={id => {
        setPage('mine')
        setSubmitted(true)
        setSelected(id)
      }} />
      : <RequestList page={page} email={user.email} onSelect={id => { setSubmitted(false); setSelected(id) }} />}
  </>
}

function RequestList({ page, email, onSelect }: {
  page: Exclude<Page, 'create'>; email: string; onSelect: (id: string) => void
}) {
  const [revision, setRevision] = useState(0)
  const path = { mine: '/requests', inbox: '/approval-inbox', all: '/requests?all=true' }[page]
  const query = useQuery<AccessRequest[]>(path, email, revision)
  const title = { mine: 'My requests', inbox: 'Approval inbox', all: 'All requests' }[page]
  const empty = { mine: 'You have no access requests yet.', inbox: 'No requests are waiting for your decision.',
    all: 'No access requests have been submitted.' }[page]
  return <section className="panel">
    <h2>{title}</h2>
    {query.state === 'loading' && <Loading />}
    {query.state === 'error' && <ErrorNotice message={query.message} retry={() => setRevision(x => x + 1)} />}
    {query.state === 'ready' && (query.data.length === 0 ? <p className="empty">{empty}</p> :
      <ul className="request-list">{query.data.map(request => <li key={request.id}>
        <button type="button" className="request-row" onClick={() => onSelect(request.id)}>
          <span><strong>{request.application}</strong><small>{environmentLabels[request.environment]} · {request.accessLevel}</small></span>
          <span className="row-meta"><small>{request.requester}</small><small>{formatDate(request.createdAtUtc)}</small></span>
          <StatusBadge status={request.status} /><span aria-hidden="true" className="chevron">→</span>
        </button>
      </li>)}</ul>)}
  </section>
}

createRoot(document.getElementById('root')!).render(<App />)
