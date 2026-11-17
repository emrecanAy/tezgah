import { useEffect, useState } from 'react'
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AdminPage } from './admin/AdminPage'
import { ApiError, api, getToken, setSignedOutHandler, setToken } from './api/client'
import type { Me, ModuleKey } from './api/types'
import { Shell } from './components/Shell'
import { AppointmentsPage } from './modules/appointments/AppointmentsPage'
import { CustomersPage } from './modules/customers/CustomersPage'
import { FleetPage } from './modules/fleet/FleetPage'
import { VehiclePage } from './modules/fleet/VehiclePage'
import { InventoryPage } from './modules/inventory/InventoryPage'
import { LoyaltyPage } from './modules/loyalty/LoyaltyPage'
import { OrdersPage } from './modules/orders/OrdersPage'
import { HomePage } from './pages/HomePage'
import { LoginPage } from './pages/LoginPage'

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

export function App() {
  const [signedIn, setSignedIn] = useState(() => getToken() !== null)

  useEffect(() => {
    setSignedOutHandler(() => setSignedIn(false))
  }, [])

  function signOut() {
    setToken(null)
    queryClient.clear()
    setSignedIn(false)
  }

  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        {signedIn ? <SignedIn onSignOut={signOut} /> : <LoginPage onSignedIn={() => setSignedIn(true)} />}
      </BrowserRouter>
    </QueryClientProvider>
  )
}

function SignedIn({ onSignOut }: { onSignOut: () => void }) {
  const { data: me } = useQuery({ queryKey: ['me'], queryFn: () => api<Me>('/me') })
  if (!me) return null

  return (
    <Routes>
      <Route element={<Shell me={me} onSignOut={onSignOut} />}>
        {me.company ? (
          <>
            <Route index element={<HomePage me={me} />} />
            {me.modules.flatMap((module) =>
              MODULE_ROUTES[module.key].map((route) => <Route key={route.path} path={route.path} element={route.element} />),
            )}
          </>
        ) : (
          <Route index element={<AdminPage />} />
        )}
        {/* A module the company does not have has no route; its address leads home. */}
        <Route path="*" element={<Navigate to="/" replace />} />
      </Route>
    </Routes>
  )
}
