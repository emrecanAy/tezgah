export type ModuleKey = 'customers' | 'loyalty' | 'appointments' | 'fleet' | 'inventory' | 'orders'

export type Role = 'platform_admin' | 'owner' | 'manager' | 'staff'

export interface Me {
  user: { id: string; fullName: string; email: string; role: Role; twoStepEnabled: boolean; mustChangePassword: boolean }
  company: { id: string; name: string; sector: string } | null
  modules: { key: ModuleKey; name: string; description: string }[]
  /** What this user may do, e.g. "orders.cancel". The API checks again; this only hides what would be refused. */
  permissions: string[]
}

export interface SignInResponse {
  session: import('./client').Session | null
  twoStepTicket: string | null
}

export interface TeamMember {
  id: string
  fullName: string
  email: string
  role: Exclude<Role, 'platform_admin'>
  isActive: boolean
  twoStepEnabled: boolean
  mustChangePassword: boolean
  lastSignInAt: string | null
}

export interface DeviceSession {
  id: string
  device: string | null
  startedAt: string
  lastUsedAt: string
  isCurrent: boolean
}

export interface CatalogModule {
  key: ModuleKey
  name: string
  description: string
  dependsOn: ModuleKey[]
}

export interface CompanyAccount {
  id: string
  name: string
  sector: string
  createdAt: string
  modules: ModuleKey[]
  ownerEmail: string | null
}

export interface Customer {
  id: string
  fullName: string
  phone: string
  email: string | null
  notes: string | null
  createdAt: string
}

/** What earns a stamp. There is no "by hand": a stamp always stands for a recorded visit or sale. */
export type EarnRule = 'appointment' | 'order' | 'item'

export interface LoyaltyProgram {
  id: string
  name: string
  stampsRequired: number
  reward: string
  earnRule: EarnRule
  /** Only items of this menu category count; null for every item. */
  category: string | null
  isActive: boolean
  cardCount: number
}

export interface LoyaltyCard {
  id: string
  programId: string
  customerId: string
  customerName: string
  customerPhone: string
  stamps: number
  stampsRequired: number
  rewardsAvailable: number
  rewardsRedeemed: number
  updatedAt: string
}

export interface Service {
  id: string
  name: string
  durationMinutes: number
  price: number
  isActive: boolean
}

export interface StaffMember {
  id: string
  name: string
  isActive: boolean
}

export type AppointmentStatus = 'scheduled' | 'completed' | 'cancelled' | 'noshow'

export interface Appointment {
  id: string
  customerId: string
  customerName: string
  serviceId: string
  serviceName: string
  staffId: string
  staffName: string
  startsAt: string
  endsAt: string
  status: AppointmentStatus
  notes: string | null
}

export interface FleetAlert {
  vehicleId: string
  plate: string
  kind: 'inspection' | 'insurance' | 'maintenance'
  dueOn: string
  daysLeft: number
}

export interface Vehicle {
  id: string
  plate: string
  brand: string
  model: string
  year: number
  odometerKm: number
  driver: string | null
  inspectionDueOn: string | null
  insuranceDueOn: string | null
  isActive: boolean
  alerts: FleetAlert[]
}

export interface VehicleLogbook {
  vehicle: Vehicle
  maintenance: { id: string; performedOn: string; description: string; cost: number; odometerKm: number; nextDueOn: string | null }[]
  fuel: { id: string; filledOn: string; liters: number; totalCost: number; odometerKm: number }[]
  averageConsumption: number | null
  fuelCost: number
  maintenanceCost: number
}

export interface Product {
  id: string
  name: string
  unit: string
  stock: number
  reorderLevel: number
  isActive: boolean
  runningLow: boolean
}

export interface StockMovement {
  kind: 'in' | 'out'
  quantity: number
  note: string | null
  createdBy: string
  createdAt: string
}

export interface MenuItem {
  id: string
  name: string
  category: string
  price: number
  isAvailable: boolean
}

export interface OrderLine {
  name: string
  category: string
  unitPrice: number
  quantity: number
  freeQuantity: number
  amount: number
}

export interface Order {
  id: string
  customerId: string | null
  customerName: string | null
  status: 'placed' | 'cancelled'
  lines: OrderLine[]
  subtotal: number
  discount: number
  total: number
  rewardNote: string | null
  placedBy: string
  placedById: string | null
  placedAt: string
}

export interface RewardOffer {
  cardId: string
  programName: string
  reward: string
  /** The menu category the reward makes free; null for any item. */
  category: string | null
  available: number
}
