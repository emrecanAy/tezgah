import { useQuery } from '@tanstack/react-query'
import { api } from '../api/client'
import type { Me } from '../api/types'

/** The signed-in user, from the cache the shell already filled. */
export function useMe(): Me | undefined {
  return useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/me'), staleTime: Infinity }).data
}

/**
 * Whether to offer an action. Only hides what the API would refuse anyway: every endpoint checks
 * the same permission on the server.
 */
export function useCan(permission: string): boolean {
  return useMe()?.permissions.includes(permission) ?? false
}

/** Staff may cancel their own order for 15 minutes after placing it; the API applies the same rule. */
export function mayCancelOrder(me: Me | undefined, order: { placedById: string | null; placedAt: string }, now = Date.now()) {
  if (!me) return false
  if (me.permissions.includes('orders.cancel')) return true
  return order.placedById === me.user.id && now - new Date(order.placedAt).getTime() <= 15 * 60_000
}

export const ROLE_NAMES: Record<string, string> = {
  owner: 'Patron',
  manager: 'Yönetici',
  staff: 'Personel',
  platform_admin: 'Platform yöneticisi',
}
