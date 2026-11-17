import { type FormEvent, type ReactNode, useEffect, useRef } from 'react'
import { useMutation } from '@tanstack/react-query'
import './Dialog.css'

interface DialogProps {
  title: string
  onClose: () => void
  children: ReactNode
}

/** A modal on the native dialog element: focus trapping, Escape and the backdrop come from the browser. */
export function Dialog({ title, onClose, children }: DialogProps) {
  const ref = useRef<HTMLDialogElement>(null)

  useEffect(() => {
    const dialog = ref.current
    dialog?.showModal()
    return () => dialog?.close()
  }, [])

  return (
    <dialog
      ref={ref}
      className="dialog"
      onClose={onClose}
      onClick={(event) => {
        // A click that lands on the dialog element itself is a click on the backdrop.
        if (event.target === ref.current) onClose()
      }}
    >
      <div className="dialog__body">
        <header className="dialog__header">
          <h2>{title}</h2>
          <button type="button" className="dialog__close" onClick={onClose} aria-label="Kapat">
            ×
          </button>
        </header>
        {children}
      </div>
    </dialog>
  )
}

interface FormDialogProps {
  title: string
  submitLabel: string
  /** Receives the form's fields; resolves when the change is saved. */
  onSubmit: (fields: FormData) => Promise<unknown>
  /** Called after a successful save, typically to close the dialog and refresh a list. */
  onSaved: () => void
  onClose: () => void
  children: ReactNode
}

/** The dialog every "add" and "edit" action uses: fields, a save button, and the API's error if it refuses. */
export function FormDialog({ title, submitLabel, onSubmit, onSaved, onClose, children }: FormDialogProps) {
  const save = useMutation({ mutationFn: onSubmit, onSuccess: onSaved })

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    save.mutate(new FormData(event.currentTarget))
  }

  return (
    <Dialog title={title} onClose={onClose}>
      <form className="form" onSubmit={submit}>
        {children}
        {save.error && (
          <p className="form-error" role="alert">
            {save.error.message}
          </p>
        )}
        <div className="form-actions">
          <button type="button" className="button button--quiet" onClick={onClose}>
            Vazgeç
          </button>
          <button type="submit" className="button" disabled={save.isPending}>
            {save.isPending ? 'Kaydediliyor' : submitLabel}
          </button>
        </div>
      </form>
    </Dialog>
  )
}

/** Reads a text field, trimmed; empty becomes null so optional values are not sent as "". */
export function text(fields: FormData, name: string): string | null {
  const value = fields.get(name)
  return typeof value === 'string' && value.trim() !== '' ? value.trim() : null
}

/** Reads a number field. Turkish keyboards type decimals with a comma. */
export function num(fields: FormData, name: string): number {
  return Number((text(fields, name) ?? '0').replace(',', '.'))
}
