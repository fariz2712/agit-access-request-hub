import { statusLabels } from '../types'
import type { Status } from '../types'

export function StatusBadge({ status }: { status: Status }) {
  return <span className={'status ' + status}>{statusLabels[status]}</span>
}

export function ErrorNotice({ message, retry }: { message: string; retry?: () => void }) {
  return <div className="notice error" role="alert"><span>{message}</span>
    {retry && <button type="button" className="secondary" onClick={retry}>Try again</button>}
  </div>
}

export function Loading() {
  return <p className="empty" role="status">Loading…</p>
}
