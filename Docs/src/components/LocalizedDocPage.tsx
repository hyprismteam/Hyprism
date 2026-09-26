// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Layout from '@theme/Layout'
import MDXContent from '@theme/MDXContent'
import TOC from '@theme/TOC'
import React, { type ComponentType } from 'react'
import { useDocsLocale } from '../context/locale'
import { routeToUrl, useLocalizedDocsData, type NavigationItem } from '../data'
import { dictionaries, type Locale } from '../i18n'
import DocsSidebar from './DocsSidebar'
import Link from '@docusaurus/Link'
import SeoHead from './SeoHead'

type TocItem = Readonly<{
  value: string
  id: string
  level: number
}>

type MdxContent = ComponentType & Readonly<{
  contentTitle?: string
  frontMatter: Readonly<{
    description?: string
    title?: string
  }>
  metadata: Readonly<{
    description?: string
    title?: string
  }>
  toc: TocItem[]
}>

type LocalizedDocPageProps = Readonly<{
  en: MdxContent
  ru: MdxContent
  pageKey: string
}>

type FlatLink = Readonly<{
  label: string
  route: string
}>

function flattenNavigation(items: NavigationItem[]): FlatLink[] {
  return items.flatMap(item =>
    item.type === 'link'
      ? [{ label: item.label, route: item.route }]
      : flattenNavigation(item.items)
  )
}

function PageNavigation({ pageKey, locale }: Readonly<{
  pageKey: string
  locale: Locale
}>) {
  const { navigation } = useLocalizedDocsData()
  const dictionary = dictionaries[locale]
  const links = flattenNavigation(navigation[locale])
  const index = links.findIndex(link => link.route === pageKey)
  const previous = index > 0 ? links[index - 1] : undefined
  const next = index >= 0 ? links[index + 1] : undefined

  return (
    <nav className="pagination-nav" aria-label="Docs pages navigation">
      {previous ? (
        <Link className="pagination-nav__link pagination-nav__link--prev" to={routeToUrl(previous.route)}>
          <div className="pagination-nav__sublabel">{dictionary.previous}</div>
          <div className="pagination-nav__label">{previous.label}</div>
        </Link>
      ) : <span />}
      {next && (
        <Link className="pagination-nav__link pagination-nav__link--next" to={routeToUrl(next.route)}>
          <div className="pagination-nav__sublabel">{dictionary.next}</div>
          <div className="pagination-nav__label">{next.label}</div>
        </Link>
      )}
    </nav>
  )
}

export default function LocalizedDocPage({ en, ru, pageKey }: LocalizedDocPageProps) {
  const { locale } = useDocsLocale()
  const dictionary = dictionaries[locale]
  const Content = locale === 'ru' ? ru : en
  const title = Content.metadata.title || Content.frontMatter.title || Content.contentTitle || 'Hyprism'
  const description = Content.metadata.description || Content.frontMatter.description
  const sourcePath = pageKey ? `${pageKey}.mdx` : 'index.mdx'
  const editUrl = `https://github.com/hyprismteam/Hyprism/edit/main/Docs/content/${locale}/${sourcePath}`

  const canonicalPath = routeToUrl(pageKey)

  return (
    <Layout title={title} description={description}>
      <SeoHead
        title={title}
        description={description}
        canonicalPath={canonicalPath}
        locale={locale}
        isHome={!pageKey}
      />
      <div className={`hyprism-docs-shell${pageKey ? '' : ' hyprism-docs-home'}`}>
        <div className="hyprism-docs-layout">
          <DocsSidebar />
          <main id="main" className="hyprism-doc-main">
            <article className="theme-doc-markdown markdown">
              <MDXContent>
                <Content />
              </MDXContent>
            </article>
            <a className="hyprism-edit-link" href={editUrl} target="_blank" rel="noreferrer">
              <svg viewBox="0 0 24 24" aria-hidden="true">
                <path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61-.546-1.387-1.333-1.756-1.333-1.756-1.089-.745.084-.729.084-.729 1.205.084 1.84 1.237 1.84 1.237 1.07 1.834 2.809 1.304 3.495.997.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.21 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12" />
              </svg>
              {dictionary.editPage}
            </a>
            <PageNavigation pageKey={pageKey} locale={locale} />
          </main>
          {Content.toc.length > 0 && (
            <aside className="hyprism-doc-toc">
              <strong>{dictionary.toc}</strong>
              <TOC toc={Content.toc} minHeadingLevel={2} maxHeadingLevel={3} />
            </aside>
          )}
        </div>
      </div>
    </Layout>
  )
}
