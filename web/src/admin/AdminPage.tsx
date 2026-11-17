import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { api, send } from '../api/client'
import type { CatalogModule, CompanyAccount, ModuleKey } from '../api/types'
import { Dialog, FormDialog, text } from '../components/Dialog'
import { toggleModule } from '../lib/modules'
import './AdminPage.css'

export function AdminPage() {
  const queryClient = useQueryClient()
  const [adding, setAdding] = useState(false)
  const [newModules, setNewModules] = useState<ModuleKey[]>([])
  const [opened, setOpened] = useState<{ company: CompanyAccount; ownerTemporaryPassword: string }>()

  const { data: catalog } = useQuery({ queryKey: ['admin', 'modules'], queryFn: () => api<CatalogModule[]>('/admin/modules') })
  const { data: companies } = useQuery({ queryKey: ['admin', 'companies'], queryFn: () => api<CompanyAccount[]>('/admin/companies') })

  const setModules = useMutation({
    mutationFn: ({ companyId, modules }: { companyId: string; modules: ModuleKey[] }) =>
      send('PUT', `/admin/companies/${companyId}/modules`, { modules }),
    onSettled: () => queryClient.invalidateQueries({ queryKey: ['admin', 'companies'] }),
  })

  if (!catalog || !companies) return null

  return (
    <>
      <header className="page-header">
        <div>
          <h1>Firmalar</h1>
          <p>Her firmaya ihtiyacı olan modülleri verin. Kapatılan modülün verisi silinmez; yeniden açınca yerinde durur.</p>
        </div>
        <button type="button" className="button" onClick={() => { setNewModules([]); setAdding(true) }}>
          Firma ekle
        </button>
      </header>

      {setModules.error && (
        <p className="form-error" role="alert">
          {setModules.error.message}
        </p>
      )}

      <ul className="companies">
        {companies.map((company) => (
          <li key={company.id} className="panel company">
            <div className="company__head">
              <h2>{company.name}</h2>
              <span className="muted">
                {company.sector}
                {company.ownerEmail ? `, ${company.ownerEmail}` : ''}
              </span>
            </div>
            <ModulePicker
              catalog={catalog}
              selection={company.modules}
              disabled={setModules.isPending}
              onChange={(modules) => setModules.mutate({ companyId: company.id, modules })}
            />
          </li>
        ))}
      </ul>

      {adding && (
        <FormDialog
          title="Firma ekle"
          submitLabel="Firmayı aç"
          onSubmit={(fields) =>
            send<{ company: CompanyAccount; ownerTemporaryPassword: string }>('POST', '/admin/companies', {
              name: text(fields, 'name'),
              sector: text(fields, 'sector'),
              ownerFullName: text(fields, 'ownerFullName'),
              ownerEmail: text(fields, 'ownerEmail'),
              modules: newModules,
            }).then(setOpened)
          }
          onSaved={() => {
            setAdding(false)
            void queryClient.invalidateQueries({ queryKey: ['admin', 'companies'] })
          }}
          onClose={() => setAdding(false)}
        >
          <div className="field-row">
            <label className="field">
              Firma adı
              <input name="name" required />
            </label>
            <label className="field">
              Sektör
              <input name="sector" required placeholder="Kuaför, kafe, lojistik..." />
            </label>
          </div>
          <label className="field">
            Yetkili adı soyadı
            <input name="ownerFullName" required />
          </label>
          <label className="field">
            Yetkili e-postası <small>Geçici bir şifre üretilir; yetkili ilk girişte kendi şifresini belirler.</small>
            <input name="ownerEmail" type="email" required />
          </label>
          <fieldset className="company__modules-field">
            <legend>Modüller</legend>
            <ModulePicker catalog={catalog} selection={newModules} disabled={false} onChange={setNewModules} />
          </fieldset>
        </FormDialog>
      )}

      {opened && (
        <Dialog title={`${opened.company.name} açıldı`} onClose={() => setOpened(undefined)}>
          <p>
            Yetkilinin geçici şifresi aşağıda. {opened.company.ownerEmail} ile bir kez girecek ve hemen kendi şifresini
            belirleyecek; siz o şifreyi hiç bilmeyeceksiniz. Bu şifre bir daha gösterilmeyecek.
          </p>
          <p className="temporary-password num">{opened.ownerTemporaryPassword}</p>
          <div className="form-actions">
            <button type="button" className="button" onClick={() => setOpened(undefined)}>
              Tamam
            </button>
          </div>
        </Dialog>
      )}
    </>
  )
}

interface ModulePickerProps {
  catalog: CatalogModule[]
  selection: ModuleKey[]
  disabled: boolean
  onChange: (modules: ModuleKey[]) => void
}

function ModulePicker({ catalog, selection, disabled, onChange }: ModulePickerProps) {
  const nameOf = (key: ModuleKey) => catalog.find((module) => module.key === key)?.name ?? key

  return (
    <ul className="module-picker">
      {catalog.map((module) => (
        <li key={module.key}>
          <label className="check">
            <input
              type="checkbox"
              checked={selection.includes(module.key)}
              disabled={disabled}
              onChange={(event) => onChange(toggleModule(catalog, selection, module.key, event.target.checked))}
            />
            <span>
              <strong>{module.name}</strong>
              <small className="muted">
                {module.description}
                {module.dependsOn.length > 0 && ` ${module.dependsOn.map(nameOf).join(', ')} modülüyle birlikte çalışır.`}
              </small>
            </span>
          </label>
        </li>
      ))}
    </ul>
  )
}
