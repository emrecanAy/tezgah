/**
 * The access token lives only in this module's memory: a script injected into the page cannot find
 * it in storage, and closing the tab forgets it. The refresh token is in an HttpOnly cookie the page
 * cannot read at all; the browser sends it to /api/auth and nowhere else.
 */
let accessToken: string | null = null

export interface Session {
  accessToken: string
  expiresAt: string
  mustChangePassword: boolean
}

export function setSession(session: Session | null): void {
  accessToken = session?.accessToken ?? null
}

/** An error the API explained with a problem document. `code` is stable; `message` is ready to show. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly code: string | undefined,
    message: string,
  ) {
    super(message)
  }
}

/** What the user reads for each error code. The API's own `detail` is English and meant for developers. */
const MESSAGES: Record<string, string> = {
  invalid_credentials: 'E-posta adresi veya şifre hatalı.',
  too_many_attempts: 'Çok fazla hatalı deneme yapıldı. 15 dakika sonra tekrar deneyin.',
  too_many_requests: 'Bu adresten çok fazla giriş denemesi geldi. Bir dakika bekleyin.',
  account_disabled: 'Bu hesap firmanız tarafından kapatıldı.',
  invalid_code: 'Kod hatalı ya da daha önce kullanılmış.',
  ticket_expired: 'Giriş çok uzun sürdü. Şifreyle baştan başlayın.',
  weak_password: 'Şifre en az 12 karakter olmalı ve kolay tahmin edilmemeli. Birkaç kelimelik bir cümle iyi bir şifredir.',
  wrong_password: 'Mevcut şifre hatalı.',
  same_password: 'Yeni şifre eskisiyle aynı olamaz.',
  two_step_already_enabled: 'İki adımlı giriş zaten açık.',
  two_step_not_started: 'Önce kurulumu başlatın.',
  session_not_found: 'Bu oturum bulunamadı.',
  invalid_member: 'Ad ve geçerli bir e-posta adresi gerekli.',
  invalid_role: 'Rol patron, yönetici veya personel olabilir.',
  email_in_use: 'Bu e-posta adresiyle bir hesap zaten var.',
  member_not_found: 'Bu kişi firmanızda değil.',
  cannot_change_self: 'Kendi rolünüzü veya erişiminizi başka bir patron değiştirebilir.',
  last_owner: 'Firmada en az bir aktif patron kalmalı.',
  cancel_not_allowed: 'Bu siparişi yalnızca yönetici iptal edebilir. Personel kendi siparişini 15 dakika içinde iptal edebilir.',
  module_not_enabled: 'Bu modül firmanız için açık değil.',
  invalid_module_selection: 'Bu modül seçimi geçerli değil: bağımlı olduğu modül de seçilmeli.',
  invalid_company: 'Firma adı, sektör, yetkili adı ve geçerli bir e-posta gerekli.',
  invalid_customer: 'Ad soyad ve en az 10 haneli bir telefon numarası gerekli.',
  phone_in_use: 'Bu telefon numarasıyla kayıtlı bir müşteri zaten var.',
  customer_not_found: 'Müşteri bulunamadı.',
  invalid_program: 'Programın bir adı ve bir ödülü olmalı.',
  invalid_stamp_count: 'Bir kart 2 ile 20 arasında damga alabilir.',
  program_inactive: 'Bu program duraklatılmış.',
  no_reward_available: 'Bu kartta kullanılacak ödül kalmadı.',
  invalid_earn_rule: 'Damganın nasıl kazanılacağını seçin.',
  card_not_found: 'Sadakat kartı bulunamadı.',
  reward_not_for_orders: 'Bu programın ödülü siparişte kullanılamaz.',
  redeem_at_till: 'Bu programın ödülü kasada, siparişe uygulanarak kullanılır.',
  invalid_menu_item: 'Ürünün adı ve kategorisi gerekli.',
  invalid_price: 'Fiyat sıfır veya pozitif, en fazla iki ondalıklı olmalı.',
  menu_item_name_in_use: 'Menüde bu adla bir ürün zaten var.',
  menu_item_not_found: 'Menüde böyle bir ürün yok.',
  invalid_order: 'Siparişte 1 ile 50 arası ürün olmalı; her biri 1 ile 99 adet.',
  menu_item_unavailable: 'Siparişteki bir ürün artık satışta değil. Menüyü yenileyin.',
  reward_needs_customer: 'Ödül kullanmak için müşteri seçin.',
  reward_not_applicable: 'Siparişte bu ödülün geçerli olduğu bir ürün yok.',
  order_not_found: 'Sipariş bulunamadı.',
  order_cancelled: 'Bu sipariş zaten iptal edilmiş.',
  order_id_in_use: 'Sipariş kaydedilemedi. Lütfen tekrar deneyin.',
  invalid_service: 'Hizmetin adı, 5 dakika ile 8 saat arasında bir süresi ve geçerli bir fiyatı olmalı.',
  invalid_staff: 'Personelin adı gerekli.',
  service_or_staff_not_found: 'Seçilen hizmet veya personel kullanılamıyor.',
  in_the_past: 'Geçmiş bir saate randevu verilemez.',
  slot_taken: 'Bu personelin o saatte başka bir randevusu var.',
  appointment_closed: 'Bu randevu zaten kapatılmış.',
  invalid_vehicle: 'Plaka, marka ve model gerekli.',
  invalid_year: 'Model yılı geçerli değil.',
  odometer_backwards: 'Kilometre, aracın mevcut kilometresinden düşük olamaz.',
  plate_in_use: 'Bu plakayla kayıtlı bir araç zaten var.',
  invalid_maintenance: 'Yapılan işi yazın; tutar negatif olamaz.',
  invalid_fuel_entry: 'Litre sıfırdan büyük olmalı; tutar negatif olamaz.',
  invalid_product: 'Ürünün adı gerekli.',
  invalid_unit: 'Listeden bir ölçü birimi seçin.',
  invalid_reorder_level: 'Uyarı seviyesi ürünün birimine uymuyor: adet, paket, koli ve şişede tam sayı girin.',
  unit_locked: 'Stokta miktar varken ölçü birimi değiştirilemez.',
  product_name_in_use: 'Bu adla kayıtlı bir ürün zaten var.',
  invalid_quantity: 'Miktar sıfırdan büyük olmalı; adet, paket, koli ve şişe ile sayılan ürünlerde tam sayı girin.',
  insufficient_stock: 'Stokta bu kadar ürün yok.',
  concurrent_update: 'Kayıt siz çalışırken değişti. Sayfayı yenileyip tekrar deneyin.',
}

/** Called when the session cannot be renewed any more, so the app can fall back to the sign-in screen. */
let onSignedOut: () => void = () => {}

export function setSignedOutHandler(handler: () => void): void {
  onSignedOut = handler
}

// Sent with every request. The auth endpoints refuse requests without it, which is what keeps another
// site from using the refresh cookie: a cross-site form cannot set it, a cross-site script would need
// a CORS permission the API never gives.
const CLIENT_HEADER = { 'X-Requested-With': 'tezgah' }

let refreshing: Promise<boolean> | null = null

/**
 * Trades the refresh cookie for a new access token. Each refresh token works once, so two tabs must
 * not refresh with the same cookie at the same moment: a Web Lock makes them take turns, and the
 * second one simply uses the cookie the first one received.
 */
export function refreshSession(): Promise<boolean> {
  refreshing ??= withLock(async () => {
    const response = await fetch('/api/auth/refresh', { method: 'POST', headers: CLIENT_HEADER, credentials: 'same-origin' })
    if (!response.ok) {
      setSession(null)
      return false
    }
    setSession((await response.json()) as Session)
    return true
  }).finally(() => {
    refreshing = null
  })
  return refreshing
}

function withLock<T>(work: () => Promise<T>): Promise<T> {
  return typeof navigator !== 'undefined' && navigator.locks
    ? navigator.locks.request('tezgah-refresh', work)
    : work()
}

export async function api<T>(path: string, init: RequestInit = {}, retried = false): Promise<T> {
  const response = await fetch(`/api${path}`, {
    ...init,
    credentials: 'same-origin',
    headers: { ...init.headers, ...CLIENT_HEADER, ...(accessToken ? { Authorization: `Bearer ${accessToken}` } : {}) },
  })

  if (response.ok) {
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
  }

  // The access token ran out (or was ended by a password or role change): renew once and repeat.
  const isAuthCall = path.startsWith('/auth/')
  if (response.status === 401 && !isAuthCall && !retried) {
    if (await refreshSession()) return api<T>(path, init, true)
    onSignedOut()
  }

  const problem = (await response.json().catch(() => null)) as { code?: string } | null
  throw new ApiError(
    response.status,
    problem?.code,
    (problem?.code && MESSAGES[problem.code]) ??
      (response.status === 403 ? 'Bu işlem için yetkiniz yok.' : 'İşlem tamamlanamadı. Lütfen tekrar deneyin.'),
  )
}

export function send<T>(method: 'POST' | 'PUT' | 'DELETE', path: string, body?: unknown): Promise<T> {
  return api<T>(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: method === 'DELETE' ? undefined : JSON.stringify(body ?? {}),
  })
}

/** Ends the session on the server too, so the refresh cookie is worthless even if it was copied. */
export async function signOut(): Promise<void> {
  await fetch('/api/auth/sign-out', { method: 'POST', headers: CLIENT_HEADER, credentials: 'same-origin' }).catch(() => {})
  setSession(null)
}
