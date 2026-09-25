// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import Head from '@docusaurus/Head'
import useDocusaurusContext from '@docusaurus/useDocusaurusContext'
import type { Locale } from '../i18n'

const localeMeta: Record<Locale, Readonly<{
  language: string
  ogLocale: string
}>> = {
  en: {
    language: 'English',
    ogLocale: 'en_US'
  },
  ru: {
    language: 'Russian',
    ogLocale: 'ru_RU'
  }
}

function siteBaseUrl(siteUrl: string, baseUrl: string): URL {
  return new URL(baseUrl, siteUrl)
}

function canonicalUrl(siteUrl: string, baseUrl: string, route: string): string {
  const base = siteBaseUrl(siteUrl, baseUrl)
  const relativeRoute = route.replace(/^\/+/, '')
  return new URL(relativeRoute, base).toString()
}

function keywords(title: string, description?: string): string {
  const terms = new Set([
    'Hyprism',
    'Hyprism Launcher',
    'Hytale',
    'open source',
    'documentation',
    ...title.split(/[^\p{L}\p{N}]+/u),
    ...(description ?? '').split(/[^\p{L}\p{N}]+/u)
  ])

  return [...terms]
    .map(term => term.trim())
    .filter(term => term.length > 2)
    .join(', ')
}

type Props = Readonly<{
  title: string
  description?: string
  canonicalPath: string
  locale: Locale
  isHome: boolean
}>

export default function SeoHead({ title, description, canonicalPath, locale, isHome }: Props) {
  const { siteConfig } = useDocusaurusContext()
  const base = siteBaseUrl(siteConfig.url, siteConfig.baseUrl)
  const canonical = canonicalUrl(siteConfig.url, siteConfig.baseUrl, canonicalPath)
  const image = new URL('img/hyprism-logo.svg', base).toString()
  const meta = localeMeta[locale]
  const fullTitle = isHome ? title : `${title} | Hyprism`
  const pageKeywords = keywords(title, description)

  const structuredData = isHome
    ? {
        '@context': 'https://schema.org',
        '@type': 'WebSite',
        name: title,
        description,
        url: canonical,
        inLanguage: locale,
        publisher: {
          '@type': 'Organization',
          name: 'Hyprism Team',
          url: 'https://hyprism.org',
          sameAs: ['https://github.com/hyprismteam/Hyprism']
        }
      }
    : {
        '@context': 'https://schema.org',
        '@type': 'TechArticle',
        headline: title,
        name: title,
        description,
        url: canonical,
        inLanguage: locale,
        mainEntityOfPage: {
          '@type': 'WebPage',
          '@id': canonical
        },
        isPartOf: {
          '@type': 'WebSite',
          name: 'Hyprism Documentation',
          url: base.toString()
        },
        author: {
          '@type': 'Organization',
          name: 'Hyprism Team',
          url: 'https://hyprism.org'
        },
        publisher: {
          '@type': 'Organization',
          name: 'Hyprism Team',
          url: 'https://hyprism.org',
          sameAs: ['https://github.com/hyprismteam/Hyprism']
        }
      }

  return (
    <Head>
      <title>{fullTitle}</title>
      {description && <meta name="description" content={description} />}
      <meta name="author" content="Hyprism Team" />
      <meta name="keywords" content={pageKeywords} />
      <meta name="robots" content="index, follow" />
      <meta name="language" content={meta.language} />
      <meta name="generator" content="Docusaurus" />
      <link rel="canonical" href={canonical} />

      <meta property="og:type" content={isHome ? 'website' : 'article'} />
      <meta property="og:url" content={canonical} />
      <meta property="og:title" content={fullTitle} />
      {description && <meta property="og:description" content={description} />}
      <meta property="og:site_name" content="Hyprism Documentation" />
      <meta property="og:locale" content={meta.ogLocale} />
      <meta property="og:image" content={image} />
      <meta property="og:image:alt" content="Hyprism Launcher logo" />

      <meta name="twitter:card" content="summary_large_image" />
      <meta name="twitter:title" content={fullTitle} />
      {description && <meta name="twitter:description" content={description} />}
      <meta name="twitter:image" content={image} />
      <meta name="twitter:image:alt" content="Hyprism Launcher logo" />

      <script type="application/ld+json">
        {JSON.stringify(structuredData)}
      </script>
    </Head>
  )
}
