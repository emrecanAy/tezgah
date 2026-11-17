const TOKEN_KEY = 'tezgah.token'

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
  module_not_enabled: 'Bu modül firmanız için açık değil.',
  invalid_module_selection: 'Bu modül seçimi geçerli değil: bağımlı olduğu modül de seçilmeli.',
  invalid_company: 'Firma adı, sektör, yetkili adı ve geçerli bir e-posta gerekli.',
  weak_password: 'Şifre en az 8 karakter olmalı.',
  email_in_use: 'Bu e-posta adresi başka bir hesapta kullanılıyor.',
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
  invalid_product: 'Ürünün adı ve birimi gerekli; miktarlar negatif olamaz.',
  product_name_in_use: 'Bu adla kayıtlı bir ürün zaten var.',
  invalid_quantity: 'Miktar sıfırdan büyük olmalı.',
  insufficient_stock: 'Stokta bu kadar ürün yok.',
  concurrent_update: 'Kayıt siz çalışırken değişti. Sayfayı yenileyip tekrar deneyin.',
}

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY)
}

export function setToken(token: string | null): void {
  if (token) localStorage.setItem(TOKEN_KEY, token)
  else localStorage.removeItem(TOKEN_KEY)
}

/** Called when the API rejects the stored token, so the app can fall back to the sign-in screen. */
let onSignedOut: () => void = () => {}

export function setSignedOutHandler(handler: () => void): void {
  onSignedOut = handler
}

export async function api<T>(path: string, init: RequestInit = {}): Promise<T> {
  const token = getToken()
  const response = await fetch(`/api${path}`, {
    ...init,
    headers: { ...init.headers, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
  })

  if (response.ok) {
    return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
  }

  const problem = (await response.json().catch(() => null)) as { code?: string } | null
  if (response.status === 401 && problem?.code !== 'invalid_credentials') {
    setToken(null)
    onSignedOut()
  }

  throw new ApiError(
    response.status,
    problem?.code,
    (problem?.code && MESSAGES[problem.code]) ?? 'İşlem tamamlanamadı. Lütfen tekrar deneyin.',
  )
}

export function send<T>(method: 'POST' | 'PUT', path: string, body?: unknown): Promise<T> {
  return api<T>(path, {
    method,
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body ?? {}),
  })
}
