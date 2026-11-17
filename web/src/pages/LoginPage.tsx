import { type FormEvent, useState } from 'react'
import { useMutation } from '@tanstack/react-query'
import { send, setSession } from '../api/client'
import type { SignInResponse } from '../api/types'
import './LoginPage.css'

// Demo accounts only exist when the API seeds demo data; a real installation has none of these.
const DEMO_PASSWORD = 'tezgah-demo-2026'
const DEMO_ACCOUNTS = [
  { label: 'Makas Kuaför', hint: 'Patron', email: 'makas@tezgah.local', password: DEMO_PASSWORD },
  { label: 'Makas Kuaför', hint: 'Personel (resepsiyon)', email: 'resepsiyon@tezgah.local', password: DEMO_PASSWORD },
  { label: 'Köşe Kahve', hint: 'Patron', email: 'kahve@tezgah.local', password: DEMO_PASSWORD },
  { label: 'Köşe Kahve', hint: 'Personel (kasa)', email: 'kasa@tezgah.local', password: DEMO_PASSWORD },
  { label: 'Yıldız Lojistik', hint: 'Patron', email: 'lojistik@tezgah.local', password: DEMO_PASSWORD },
  { label: 'Platform yöneticisi', hint: 'Firmalara modül verir', email: 'admin@tezgah.local', password: 'tezgah-platform-2026' },
]

export function LoginPage({ onSignedIn }: { onSignedIn: () => void }) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [ticket, setTicket] = useState<string>()
  const [code, setCode] = useState('')

  const passwordStep = useMutation({
    mutationFn: () => send<SignInResponse>('POST', '/auth/sign-in', { email, password }),
    onSuccess: (response) => {
      if (response.twoStepTicket) {
        setTicket(response.twoStepTicket)
        return
      }
      setSession(response.session)
      onSignedIn()
    },
  })

  const codeStep = useMutation({
    mutationFn: () => send<SignInResponse>('POST', '/auth/sign-in/second-step', { ticket, code }),
    onSuccess: (response) => {
      setSession(response.session)
      onSignedIn()
    },
    onError: (error) => {
      // The ticket lasts five minutes; once it is gone the password has to be given again.
      if ('code' in error && error.code === 'ticket_expired') setTicket(undefined)
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (ticket) codeStep.mutate()
    else passwordStep.mutate()
  }

  const error = (ticket ? codeStep : passwordStep).error
  const pending = passwordStep.isPending || codeStep.isPending

  return (
    <div className="login">
      <section className="login__intro">
        <p className="login__brand">Tezgâh</p>
        <h1>İşletmenize lazım olan kadarı.</h1>
        <p>
          Sadakat kartı, randevu, filo, stok, kasa. Her firma yalnızca kullandığı modülleri görür; ihtiyaç değişince
          yenisi açılır, veriler yerinde kalır.
        </p>
      </section>

      <section className="login__panel">
        <form className="form" onSubmit={submit}>
          {ticket ? (
            <>
              <h2>Doğrulama kodu</h2>
              <p className="muted">
                Doğrulama uygulamanızdaki 6 haneli kodu girin. Telefonunuz yanınızda değilse kurtarma kodlarınızdan birini
                kullanabilirsiniz.
              </p>
              <label className="field">
                Kod
                <input
                  autoComplete="one-time-code"
                  inputMode="text"
                  autoFocus
                  required
                  value={code}
                  onChange={(e) => setCode(e.target.value)}
                />
              </label>
            </>
          ) : (
            <>
              <h2>Giriş yap</h2>
              <label className="field">
                E-posta
                <input type="email" autoComplete="username" required value={email} onChange={(e) => setEmail(e.target.value)} />
              </label>
              <label className="field">
                Şifre
                <input
                  type="password"
                  autoComplete="current-password"
                  required
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </label>
            </>
          )}
          {error && (
            <p className="form-error" role="alert">
              {error.message}
            </p>
          )}
          <button type="submit" className="button" disabled={pending}>
            {pending ? 'Kontrol ediliyor' : ticket ? 'Doğrula' : 'Giriş yap'}
          </button>
          {ticket && (
            <button type="button" className="button button--quiet" onClick={() => { setTicket(undefined); setCode('') }}>
              Başka hesapla giriş yap
            </button>
          )}
        </form>

        {!ticket && (
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
        )}
      </section>
    </div>
  )
}
