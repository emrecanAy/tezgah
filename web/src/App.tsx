import { useEffect, useState } from 'react'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AdminPage } from './admin/AdminPage'
import { ApiError, api, refreshSession, setSignedOutHandler, signOut } from './api/client'
import type { Me, ModuleKey } from './api/types'
import { Shell } from './components/Shell'
import { AppointmentsPage } from './modules/appointments/AppointmentsPage'
import { CustomersPage } from './modules/customers/CustomersPage'
import { FleetPage } from './modules/fleet/FleetPage'
import { VehiclePage } from './modules/fleet/VehiclePage'
import { InventoryPage } from './modules/inventory/InventoryPage'
import { LoyaltyPage } from './modules/loyalty/LoyaltyPage'
import { OrdersPage } from './modules/orders/OrdersPage'
import { AccountPage } from './pages/AccountPage'
import { FirstPasswordPage } from './pages/FirstPasswordPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'
import { TeamPage } from './pages/TeamPage'

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // An answer from the API (not found, forbidden, ...) will not change by asking again.
      retry: (failures, error) => !(error instanceof ApiError && error.status < 500) && failures < 2,
      refetchOnWindowFocus: false,
    },
  },
})

/** The screens each module brings. Only the modules the company has are mounted as routes. */
const MODULE_ROUTES: Record<ModuleKey, { path: string; element: React.ReactElement }[]> = {
  customers: [{ path: 'customers', element: <CustomersPage /> }],
  loyalty: [{ path: 'loyalty', element: <LoyaltyPage /> }],
  appointments: [{ path: 'appointments', element: <AppointmentsPage /> }],
  fleet: [
    { path: 'fleet', element: <FleetPage /> },
    { path: 'fleet/:vehicleId', element: <VehiclePage /> },
  ],
  inventory: [{ path: 'inventory', element: <InventoryPage /> }],
  orders: [{ path: 'orders', element: <OrdersPage /> }],
}

type Status = 'starting' | 'signed-out' | 'signed-in'

export function App() {
  const [status, setStatus] = useState<Status>('starting')

  useEffect(() => {
    setSignedOutHandler(() => {
      queryClient.clear()
      setStatus('signed-out')
    })
    // The access token is not kept anywhere, so every page load starts by trading the refresh
    // cookie for a new one. No cookie, or an ended session: the sign-in screen.
    void refreshSession().then((ok) => setStatus(ok ? 'signed-in' : 'signed-out'))
  }, [])

  async function handleSignOut() {
    await signOut()
    queryClient.clear()
    setStatus('signed-out')
  }

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {status === 'signed-in' && <SignedIn onSignOut={handleSignOut} />}
        {status === 'signed-out' && <LoginPage onSignedIn={() => setStatus('signed-in')} />}
      </BrowserRouter>
    </QueryClientProvider>
  )
}

function SignedIn({ onSignOut }: { onSignOut: () => void }) {
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/me'), staleTime: Infinity })
  if (!me) return null

  // Someone else chose this password; until the user picks their own, nothing else opens.
  if (me.user.mustChangePassword) {
    return <FirstPasswordPage me={me} onSignOut={onSignOut} />
  }

  const canManageTeam = me.permissions.includes('team.manage')

  return (
    <Routes>
      <Route element={<Shell me={me} onSignOut={onSignOut} />}>
        {me.company ? (
          <>
            <Route index element={<HomePage me={me} />} />
            {me.modules.flatMap((module) =>
              MODULE_ROUTES[module.key].map((route) => <Route key={route.path} path={route.path} element={route.element} />),
            )}
            {canManageTeam && <Route path="team" element={<TeamPage />} />}
          </>
        ) : (
          <Route index element={<AdminPage />} />
        )}
        <Route path="account" element={<AccountPage me={me} />} />
        {/* A module the company does not have has no route; its address leads home. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
