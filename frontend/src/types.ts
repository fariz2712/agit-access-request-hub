export type Role = 'Requester' | 'Manager' | 'SystemOwner' | 'Auditor'
export type DemoUser = { email: string; role: Role }
export type Application = { id: number; name: string }
export type Environment = 'NonProduction' | 'Production'
export type AccessLevel = 'Read' | 'Admin'
export type Status = 'PendingManager' | 'PendingSystemOwner' | 'Approved' | 'Rejected'
export type AccessRequest = {
  id: string
  requester: string
  application: string
  environment: Environment
  accessLevel: AccessLevel
  justification: string
  status: Status
  policyVersion: string
  version: number
  createdAtUtc: string
  updatedAtUtc: string
  canDecide: boolean
}
export type AuditEvent = {
  version: number
  actor: string
  action: 'Created' | 'Approved' | 'Rejected'
  previousStatus: Status | null
  resultingStatus: Status
  occurredAtUtc: string
  reason: string | null
}
export const statusLabels: Record<Status, string> = {
  PendingManager: 'Pending manager',
  PendingSystemOwner: 'Pending system owner',
  Approved: 'Approved',
  Rejected: 'Rejected',
}
export const environmentLabels: Record<Environment, string> = {
  NonProduction: 'Non-production',
  Production: 'Production',
}
export const roleLabels: Record<Role, string> = {
  Requester: 'Requester', Manager: 'Manager', SystemOwner: 'System owner', Auditor: 'Admin / Auditor',
}
export const formatDate = (value: string) => new Intl.DateTimeFormat(undefined, {
  dateStyle: 'medium', timeStyle: 'short',
}).format(new Date(value))
