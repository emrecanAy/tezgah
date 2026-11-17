import { type FormEvent, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { type Session, send, setSession } from '../api/client'
import type { Me } from '../api/types'
import './LoginPage.css'

/**
 * The account was opened with a password someone else chose. It only lets the user in this far:
 * the API refuses everything else until the user has set a password only they know.
 */
export function FirstPasswordPage({ me, onSignOut }: { me: Me; onSignOut: () => void }) {
  const queryClient = useQueryClient()
  const [current, setCurrent] = useState('')
  const [next, setNext] = useState('')
  const [repeat, setRepeat] = useState('')

  const change = useMutation({
    mutationFn: () => send<Session>('POST', '/account/password', { currentPassword: current, newPassword: next }),
    onSuccess: async (session) => {
      setSession(session)
      await queryClient.invalidateQueries({ queryKey: ['me'] })
    },
  })

  function submit(event: FormEvent) {
    event.preventDefault()
    if (next === repeat) change.mutate()
  }

  return (
    <div className="login">
      <section className="login__intro">
        <p className="login__brand">Tezgâh</p>
        <h1>Hoş geldiniz, {me.user.fullName.split(' ')[0]}.</h1>
        <p>Size verilen geçici şifre yalnızca bu adım içindir. Devam etmek için yalnızca sizin bildiğiniz bir şifre belirleyin.</p>
      </section>

      <section className="login__panel">
        <form className="form" onSubmit={submit}>
          <h2>Şifrenizi belirleyin</h2>
          <label className="field">
            Geçici şifre
            <input type="password" autoComplete="current-password" required value={current} onChange={(e) => setCurrent(e.target.value)} />
          </label>
          <label className="field">
            Yeni şifre <small>En az 12 karakter. Birkaç kelimelik bir cümle hem güçlü hem akılda kalır.</small>
            <input type="password" autoComplete="new-password" required minLength={12} value={next} onChange={(e) => setNext(e.target.value)} />
          </label>
          <label className="field">
            Yeni şifre (tekrar)
            <input type="password" autoComplete="new-password" required value={repeat} onChange={(e) => setRepeat(e.target.value)} />
          </label>
          {repeat !== '' && next !== repeat && <p className="form-error">İki şifre aynı değil.</p>}
          {change.error && (
            <p className="form-error" role="alert">
              {change.error.message}
            </p>
          )}
          <button type="submit" className="button" disabled={change.isPending || next !== repeat}>
            Şifreyi kaydet ve devam et
          </button>
          <button type="button" className="button button--quiet" onClick={onSignOut}>
            Çıkış yap
          </button>
        </form>
      </section>
    </div>
  )
}
