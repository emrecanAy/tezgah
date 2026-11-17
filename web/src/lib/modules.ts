import type { CatalogModule, ModuleKey } from '../api/types'

/**
 * Applies one checkbox change to a selection and keeps it valid: switching a module on brings
 * the modules it depends on, switching one off takes the modules that depend on it.
 */
export function toggleModule(catalog: CatalogModule[], selection: ModuleKey[], key: ModuleKey, on: boolean): ModuleKey[] {
  const next = new Set(selection)
  if (on) {
    next.add(key)
    catalog.find((module) => module.key === key)?.dependsOn.forEach((dependency) => next.add(dependency))
  } else {
    next.delete(key)
    catalog.filter((module) => module.dependsOn.includes(key)).forEach((dependent) => next.delete(dependent.key))
  }
  return catalog.map((module) => module.key).filter((moduleKey) => next.has(moduleKey))
}
