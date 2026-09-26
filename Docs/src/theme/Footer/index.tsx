// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Link from '@docusaurus/Link'
import useBaseUrl from '@docusaurus/useBaseUrl'
import React from 'react'
import { useDocsLocale } from '../../context/locale'
import { dictionaries } from '../../i18n'

export default function Footer() {
  const { locale } = useDocsLocale()
  const dictionary = dictionaries[locale]
  const docsUrl = useBaseUrl('/')
  const logoUrl = useBaseUrl('/img/hyprism-logo.svg')

  return (
    <footer className="hyprism-footer">
      <div className="hyprism-footer-inner">
        <a
          className="hyprism-footer-cta"
          href="https://hyprismteam.github.io/"
          target="_blank"
          rel="noreferrer"
        >
          <span>
            {dictionary.footer.teamSite}
            <span className="hyprism-accent">.</span>
          </span>
          <span className="hyprism-footer-arrow" aria-hidden="true">↗</span>
        </a>
        <div className="hyprism-footer-bottom">
          <Link className="hyprism-footer-brand" to={docsUrl}>
            <img src={logoUrl} alt="" width="25" height="25" />
            <span>Hyprism Launcher</span>
          </Link>
          <span className="hyprism-footer-meta">{dictionary.footer.license}</span>
          <nav className="hyprism-footer-links" aria-label={dictionary.docsLabel}>
            <a href="https://github.com/hyprismteam/Hyprism" target="_blank" rel="noreferrer">
              {dictionary.footer.repository}
            </a>
            <a href="#main">{dictionary.footer.backToTop} ↑</a>
          </nav>
        </div>
      </div>
    </footer>
  )
}
