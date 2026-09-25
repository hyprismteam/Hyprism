// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import React, { useEffect, useRef, useState } from 'react'
import useBaseUrl from '@docusaurus/useBaseUrl'
import { useDocsLocale } from '../context/locale'
import { dictionaries, locales } from '../i18n'

export default function LanguageSwitch() {
  const { locale: activeLocale, selectLocale } = useDocsLocale()
  const dictionary = dictionaries[activeLocale]
  const [open, setOpen] = useState(false)
  const container = useRef<HTMLDivElement>(null)
  const trigger = useRef<HTMLButtonElement>(null)

  useEffect(() => {
    if (!open) return

    const dismiss = (event: PointerEvent) => {
      if (!container.current?.contains(event.target as Node)) {
        setOpen(false)
      }
    }

    const escape = (event: KeyboardEvent) => {
      if (event.key !== 'Escape') return

      setOpen(false)
      trigger.current?.focus()
    }

    document.addEventListener('pointerdown', dismiss)
    document.addEventListener('keydown', escape)

    return () => {
      document.removeEventListener('pointerdown', dismiss)
      document.removeEventListener('keydown', escape)
    }
  }, [open])

  return (
    <div
      ref={container}
      className="hyprism-language-switcher"
      data-open={open}
      onBlur={event => {
        if (!event.currentTarget.contains(event.relatedTarget)) {
          setOpen(false)
        }
      }}
    >
      <button
        ref={trigger}
        type="button"
        className="hyprism-language-trigger"
        aria-label={dictionary.languageSwitcher}
        aria-expanded={open}
        aria-controls="hyprism-language-panel"
        title={dictionary.languageSwitcher}
        onClick={() => setOpen(current => !current)}
        onKeyDown={event => {
          if (event.key === 'ArrowDown') {
            event.preventDefault()
            setOpen(true)
          }
        }}
      >
        <svg
          className="hyprism-language-globe"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="1.2"
          aria-hidden="true"
        >
          <circle cx="12" cy="12" r="9" />
          <ellipse
            className="hyprism-globe-meridian"
            cx="12"
            cy="12"
            rx="4"
            ry="9"
          />
          <path d="M3 12h18M5 6.5c4 2 10 2 14 0M5 17.5c4-2 10-2 14 0" />
        </svg>
      </button>

      <div
        id="hyprism-language-panel"
        className="hyprism-language-panel"
        inert={!open}
        aria-hidden={!open}
      >
        <div className="hyprism-language-links">
          {locales.map((locale, index) => (
            <button
              key={locale}
              type="button"
              lang={locale}
              aria-current={locale === activeLocale ? 'page' : undefined}
              style={{
                '--item-delay': `${65 + index * 28}ms`
              } as React.CSSProperties}
              onClick={() => {
                selectLocale(locale)
                setOpen(false)
              }}
            >
              <img
                src={useBaseUrl(`/assets/flags/${locale}.svg`)}
                alt=""
                width={22}
                height={15}
              />

              <span className="hyprism-language-name">
                {dictionary.languages[locale]}
              </span>

              <span
                className="hyprism-language-indicator"
                aria-hidden="true"
              >
                {locale === activeLocale ? '•' : '↗'}
              </span>
            </button>
          ))}
        </div>
      </div>
    </div>
  )
}
