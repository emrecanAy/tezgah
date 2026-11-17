import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../api/client'
import type { TeamMember } from '../api/types'
import { Dialog, FormDialog, text } from '../components/Dialog'
import { ROLE_NAMES, useMe } from '../lib/access'
import { formatDateTime } from '../lib/format'
import './TeamPage.css'

const ROLES: { role: TeamMember['role']; what: string }[] = [
  { role: 'staff', what: 'Günlük iş: sipariş girer, randevu verir, stok hareketi ve yakıt kaydı yapar.' },
  { role: 'manager', what: 'İşletmeyi kurar: menü ve fiyatlar, hizmetler, sadakat kuralları, araçlar. Her siparişi iptal edebilir.' },
  { role: 'owner', what: 'Yöneticinin her şeyi, ayrıca ekibi yönetir.' },
]

type Action = { kind: 'add' } | { kind: 'edit'; member: TeamMember } | { kind: 'password'; member: TeamMember; password: string }

/** Who works in the company and what each of them may do. Owners only. */
export function TeamPage() {
  const queryClient = useQueryClient()
  const me = useMe()
  const [action, setAction] = useState<Action>()
  const { data: team } = useQuery({ queryKey: ['team'], queryFn: () => api<TeamMember[]>('/team') })
  const refresh = () => queryClient.invalidateQueries({ queryKey: ['team'] })

  const reset = useMutation({
    mutationFn: (member: TeamMember) =>
      send<{ member: TeamMember; password: string }>('POST', `/team/${member.id}/password-reset`),
    onSuccess: ({ member, password }) => {
      setAction({ kind: 'password', member, password })
      void refresh()
    },
  })

  return (
    <>
      <header className="page-header">
        <div>
          <h1>Ekip</h1>
          <p>Herkes kendi hesabıyla girer; ne yapabileceğini rolü belirler.</p>
        </div>
        <button type="button" className="button" onClick={() => setAction({ kind: 'add' })}>
          Kişi ekle
        </button>
      </header>

      <dl className="roles">
        {ROLES.map(({ role, what }) => (
          <div key={role}>
            <dt>{ROLE_NAMES[role]}</dt>
            <dd>{what}</dd>
          </div>
        ))}
      </dl>

      {reset.error && (
        <p className="form-error" role="alert">
          {reset.error.message}
        </p>
      )}

      <div className="panel table-scroll">
        <table className="table">
          <thead>
            <tr>
              <th scope="col">Kişi</th>
              <th scope="col">Rol</th>
              <th scope="col">Son giriş</th>
              <th scope="col" />
            </tr>
          </thead>
          <tbody>
            {team?.map((member) => (
              <tr key={member.id} className={member.isActive ? undefined : 'member--inactive'}>
                <td>
                  <strong>{member.fullName}</strong>
                  {member.id === me?.user.id && <span className="muted"> (siz)</span>}
                  <small className="muted member__email">{member.email}</small>
                </td>
                <td>
                  {ROLE_NAMES[member.role]}
                  {!member.isActive && <span className="tag">Kapalı</span>}
                  {member.isActive && member.mustChangePassword && <span className="tag tag--warn">Şifresini belirlemedi</span>}
                  {member.twoStepEnabled && <span className="tag tag--ok">İki adımlı</span>}
                </td>
                <td className="muted">{member.lastSignInAt ? formatDateTime(member.lastSignInAt) : 'Hiç girmedi'}</td>
                <td className="right">
                  {member.id !== me?.user.id && (
                    <div className="row-actions">
                      <button type="button" className="button button--quiet button--small" onClick={() => setAction({ kind: 'edit', member })}>
                        Düzenle
                      </button>
                      <button
                        type="button"
                        className="button button--quiet button--small"
                        disabled={reset.isPending || !member.isActive}
                        onClick={() => reset.mutate(member)}
                      >
                        Şifre sıfırla
                      </button>
                    </div>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {action?.kind === 'add' && (
        <FormDialog
          title="Kişi ekle"
          submitLabel="Hesabı aç"
          onSubmit={(fields) =>
            send<{ member: TeamMember; password: string }>('POST', '/team', {
              fullName: text(fields, 'fullName'),
              email: text(fields, 'email'),
              role: fields.get('role'),
            }).then(({ member, password }) => setAction({ kind: 'password', member, password }))
          }
          onSaved={() => void refresh()}
          onClose={() => setAction(undefined)}
        >
          <div className="field-row">
            <label className="field">
              Ad soyad
              <input name="fullName" required autoFocus />
            </label>
            <label className="field">
              E-posta
              <input name="email" type="email" required />
            </label>
          </div>
          <RolePicker initial="staff" />
        </FormDialog>
      )}

      {action?.kind === 'edit' && (
        <FormDialog
          title={`${action.member.fullName}: düzenle`}
          submitLabel="Kaydet"
          onSubmit={(fields) =>
            send('PUT', `/team/${action.member.id}`, {
              fullName: text(fields, 'fullName'),
              role: fields.get('role'),
              isActive: fields.get('isActive') === 'on',
            })
          }
          onSaved={() => {
            setAction(undefined)
            void refresh()
          }}
          onClose={() => setAction(undefined)}
        >
          <label className="field">
            Ad soyad
            <input name="fullName" required defaultValue={action.member.fullName} />
          </label>
          <RolePicker initial={action.member.role} />
          <label className="check">
            <input type="checkbox" name="isActive" defaultChecked={action.member.isActive} />
            <span>Hesap açık. Kapatırsanız kişi o anda tüm cihazlardan çıkarılır ve bir daha giremez.</span>
          </label>
          <p className="muted">Rol değişikliği de kişiyi tüm cihazlardan çıkarır; yeniden girdiğinde yeni rolüyle çalışır.</p>
        </FormDialog>
      )}

      {action?.kind === 'password' && (
        <Dialog title="Geçici şifre" onClose={() => setAction(undefined)}>
          <p>
            <strong>{action.member.fullName}</strong> bu şifreyle bir kez girecek ve hemen kendi şifresini belirleyecek. Şifreyi
            kendisine yüz yüze ya da güvendiğiniz bir kanaldan iletin; bir daha gösterilmeyecek.
          </p>
          <p className="temporary-password num">{action.password}</p>
          <div className="form-actions">
            <button type="button" className="button" onClick={() => setAction(undefined)}>
              Tamam
            </button>
          </div>
        </Dialog>
      )}
    </>
  )
}

function RolePicker({ initial }: { initial: TeamMember['role'] }) {
  return (
    <fieldset className="role-picker">
      <legend>Rol</legend>
      {ROLES.map(({ role, what }) => (
        <label key={role} className="check">
          <input type="radio" name="role" value={role} defaultChecked={role === initial} />
          <span>
            <strong>{ROLE_NAMES[role]}</strong>. {what}
          </span>
        </label>
      ))}
    </fieldset>
  )
}
