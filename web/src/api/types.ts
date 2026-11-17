export type ModuleKey = 'customers' | 'loyalty' | 'appointments' | 'fleet' | 'inventory'

export interface Me {
  user: { id: string; fullName: string; email: string; role: 'platform_admin' | 'owner' | 'staff' }
  company: { id: string; name: string; sector: string } | null
  modules: { key: ModuleKey; name: string; description: string }[]
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

export interface LoyaltyProgram {
  id: string
  name: string
  stampsRequired: number
  reward: string
  stampOnAppointment: boolean
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
