import { type FormEvent, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { send, setToken } from '../api/client'
import './LoginPage.css'

const DEMO_ACCOUNTS = [
  { label: 'Makas Kuaför', hint: 'Müşteriler, Sadakat, Randevu', email: 'makas@tezgah.local', password: 'demo1234' },
  { label: 'Köşe Kahve', hint: 'Müşteriler, Sadakat, Stok', email: 'kahve@tezgah.local', password: 'demo1234' },
  { label: 'Yıldız Lojistik', hint: 'Filo', email: 'lojistik@tezgah.local', password: 'demo1234' },
  { label: 'Platform yöneticisi', hint: 'Firmalara modül verir', email: 'admin@tezgah.local', password: 'admin1234' },
]

export function LoginPage({ onSignedIn }: { onSignedIn: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')

  const signIn = useMutation({
    mutationFn: () => send<{ token: string }>('POST', '/auth/login', { email, password }),
    onSuccess: ({ token }) => {
      setToken(token)
      onSignedIn()
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    signIn.mutate()
  }

  return (
    <div className="login">
      <section className="login__intro">
        <p className="login__brand">Tezgâh</p>
        <h1>İşletmenize lazım olan kadarı.</h1>
        <p>
          Sadakat kartı, randevu, filo, stok. Her firma yalnızca kullandığı modülleri görür; ihtiyaç değişince yenisi
          açılır, veriler yerinde kalır.
        </p>
      </section>

      <section className="login__panel">
        <form className="form" onSubmit={submit}>
          <h2>Giriş yap</h2>
          <label className="field">
            E-posta
            <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
          </label>
          <label className="field">
            Şifre
            <input type="password" autoComplete="current-password" required value={password}
              onChange={(e) => setPassword(e.target.value)} />
          </label>
          {signIn.error && (
            <p className="form-error" role="alert">
              {signIn.error.message}
            </p>
          )}
          <button type="submit" className="button" disabled={signIn.isPending}>
            {signIn.isPending ? 'Giriş yapılıyor' : 'Giriş yap'}
          </button>
        </form>

        <div className="login__demo">
          <p>Örnek hesaplar. Birine dokunun, bilgileri dolsun:</p>
          <ul>
            {DEMO_ACCOUNTS.map((account) => (
              <li key={account.email}>
                <button type="button" onClick={() => { setEmail(account.email); setPassword(account.password) }}>
                  <strong>{account.label}</strong>
                  <span>{account.hint}</span>
                </button>
              </li>
            ))}
          </ul>
        </div>
      </section>
    </div>
  )
}
