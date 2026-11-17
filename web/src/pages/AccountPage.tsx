import { useEffect, useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { type Session, api, send, setSession } from '../api/client'
import type { DeviceSession, Me } from '../api/types'
import { Dialog, FormDialog, text } from '../components/Dialog'
import { ROLE_NAMES } from '../lib/access'
import { describeDevice } from '../lib/devices'
import { formatDateTime } from '../lib/format'
import './AccountPage.css'

/** What every user manages for themselves: password, two-step sign-in, signed-in devices. */
export function AccountPage({ me }: { me: Me }) {
  const queryClient = useQueryClient()
  const [dialog, setDialog] = useState<'password' | 'two-step' | 'disable'>()
  const [notice, setNotice] = useState<string>()
  const refreshMe = () => queryClient.invalidateQueries({ queryKey: ['me'] })

  return (
    <>
      <header className="page-header">
        <div>
          <h1>Hesabım</h1>
          <p>
            {me.user.fullName}, {me.user.email}. {ROLE_NAMES[me.user.role]}
            {me.company ? `, ${me.company.name}` : ''}.
          </p>
        </div>
      </header>

      {notice && (
        <p className="account__notice" role="status">
          {notice}
        </p>
      )}

      <section className="account__section panel">
        <div>
          <h2>Şifre</h2>
          <p className="muted">Şifreyi değiştirince bu cihaz dışındaki tüm oturumlar kapanır.</p>
        </div>
        <button type="button" className="button button--quiet" onClick={() => setDialog('password')}>
          Şifreyi değiştir
        </button>
      </section>

      <section className="account__section panel">
        <div>
          <h2>İki adımlı giriş {me.user.twoStepEnabled ? <span className="tag tag--ok">Açık</span> : <span className="tag">Kapalı</span>}</h2>
          <p className="muted">
            Şifreye ek olarak telefonunuzdaki doğrulama uygulamasından (Google Authenticator, Microsoft Authenticator, 1Password)
            bir kod istenir. Şifreniz ele geçse bile hesabınıza girilemez.
          </p>
        </div>
        {me.user.twoStepEnabled ? (
          <button type="button" className="button button--quiet" onClick={() => setDialog('disable')}>
            Kapat
          </button>
        ) : (
          <button type="button" className="button" onClick={() => setDialog('two-step')}>
            Aç
          </button>
        )}
      </section>

      <Sessions />

      {dialog === 'password' && (
        <FormDialog
          title="Şifreyi değiştir"
          submitLabel="Şifreyi kaydet"
          onSubmit={async (fields) => {
            const session = await send<Session>('POST', '/account/password', {
              currentPassword: text(fields, 'current'),
              newPassword: text(fields, 'next'),
            })
            setSession(session)
          }}
          onSaved={() => {
            setDialog(undefined)
            setNotice('Şifreniz değişti. Diğer cihazlardaki oturumlar kapatıldı.')
            void queryClient.invalidateQueries({ queryKey: ['account'] })
          }}
          onClose={() => setDialog(undefined)}
        >
          <label className="field">
            Mevcut şifre
            <input name="current" type="password" autoComplete="current-password" required />
          </label>
          <label className="field">
            Yeni şifre <small>En az 12 karakter. Birkaç kelimelik bir cümle iyi bir şifredir.</small>
            <input name="next" type="password" autoComplete="new-password" minLength={12} required />
          </label>
        </FormDialog>
      )}

      {dialog === 'two-step' && (
        <TwoStepSetup
          onClose={() => setDialog(undefined)}
          onEnabled={() => {
            void refreshMe()
          }}
        />
      )}

      {dialog === 'disable' && (
        <FormDialog
          title="İki adımlı girişi kapat"
          submitLabel="Kapat"
          onSubmit={(fields) =>
            send('POST', '/account/two-step/disable', { password: text(fields, 'password'), code: text(fields, 'code') })
          }
          onSaved={() => {
            setDialog(undefined)
            setNotice('İki adımlı giriş kapatıldı.')
            void refreshMe()
          }}
          onClose={() => setDialog(undefined)}
        >
          <p className="muted">Kapatmak için hem şifreniz hem de uygulamadaki güncel kod gerekir.</p>
          <label className="field">
            Şifre
            <input name="password" type="password" autoComplete="current-password" required />
          </label>
          <label className="field">
            Doğrulama kodu ya da kurtarma kodu
            <input name="code" autoComplete="one-time-code" required />
          </label>
        </FormDialog>
      )}
    </>
  )
}

/** Scan, confirm with a code, keep the recovery codes. */
function TwoStepSetup({ onClose, onEnabled }: { onClose: () => void; onEnabled: () => void }) {
  const [qr, setQr] = useState<string>()
  const [code, setCode] = useState('')
  const [recoveryCodes, setRecoveryCodes] = useState<string[]>()

  const setup = useQuery({
    queryKey: ['account', 'two-step-setup'],
    queryFn: () => send<{ secret: string; setupUri: string }>('POST', '/account/two-step/setup'),
    gcTime: 0,
    staleTime: Infinity,
  })

  useEffect(() => {
    // Drawn here, in the browser: the secret is never sent to a QR service.
    if (setup.data) void QRCode.toDataURL(setup.data.setupUri, { margin: 1, width: 200 }).then(setQr)
  }, [setup.data])

  const enable = useMutation({
    mutationFn: () => send<{ codes: string[] }>('POST', '/account/two-step/enable', { code }),
    onSuccess: ({ codes }) => {
      setRecoveryCodes(codes)
      onEnabled()
    },
  })

  if (recoveryCodes) {
    return (
      <Dialog title="Kurtarma kodlarınız" onClose={onClose}>
        <p>
          Telefonunuz yanınızda değilken bu kodlardan biriyle girebilirsiniz. Her kod bir kez çalışır. Bir daha
          gösterilmeyecekler: yazdırın ya da şifre yöneticinize kaydedin.
        </p>
        <ul className="recovery-codes num">
          {recoveryCodes.map((recovery) => (
            <li key={recovery}>{recovery}</li>
          ))}
        </ul>
        <div className="form-actions">
          <button type="button" className="button" onClick={onClose}>
            Kaydettim
          </button>
        </div>
      </Dialog>
    )
  }

  return (
    <Dialog title="İki adımlı girişi aç" onClose={onClose}>
      <ol className="two-step">
        <li>
          Doğrulama uygulamanızla bu kodu okutun.
          <div className="two-step__qr">{qr ? <img src={qr} alt="Doğrulama uygulaması için QR kod" width={200} height={200} /> : null}</div>
          {setup.data && (
            <p className="muted">
              Okutamıyorsanız anahtarı elle girin: <code className="two-step__secret">{setup.data.secret}</code>
            </p>
          )}
        </li>
        <li>
          Uygulamanın gösterdiği 6 haneli kodu yazın.
          <label className="field">
            Kod
            <input inputMode="numeric" autoComplete="one-time-code" maxLength={6} value={code} onChange={(e) => setCode(e.target.value)} />
          </label>
        </li>
      </ol>
      {(setup.error ?? enable.error) && (
        <p className="form-error" role="alert">
          {(setup.error ?? enable.error)?.message}
        </p>
      )}
      <div className="form-actions">
        <button type="button" className="button button--quiet" onClick={onClose}>
          Vazgeç
        </button>
        <button type="button" className="button" disabled={code.length !== 6 || enable.isPending} onClick={() => enable.mutate()}>
          Doğrula ve aç
        </button>
      </div>
    </Dialog>
  )
}

function Sessions() {
  const queryClient = useQueryClient()
  const { data: sessions } = useQuery({
    queryKey: ['account', 'sessions'],
    queryFn: () => api<DeviceSession[]>('/account/sessions'),
  })
  const end = useMutation({
    mutationFn: (id: string) => send('DELETE', `/account/sessions/${id}`),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['account', 'sessions'] }),
  })

  return (
    <section className="account__sessions">
      <h2>Açık oturumlar</h2>
      <p className="muted">Tanımadığınız bir cihaz görürseniz oturumu kapatın ve şifrenizi değiştirin.</p>
      <ul className="panel">
        {sessions?.map((session) => (
          <li key={session.id}>
            <div>
              <strong>{describeDevice(session.device)}</strong>
              <small className="muted">
                Giriş {formatDateTime(session.startedAt)}, son kullanım {formatDateTime(session.lastUsedAt)}
              </small>
            </div>
            {session.isCurrent ? (
              <span className="tag tag--ok">Bu cihaz</span>
            ) : (
              <button type="button" className="button button--quiet button--small" disabled={end.isPending} onClick={() => end.mutate(session.id)}>
                Oturumu kapat
              </button>
            )}
          </li>
        ))}
      </ul>
    </section>
  )
}
