// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import type { Plugin } from '@docusaurus/types'
import { promises as fs } from 'node:fs'
import path from 'node:path'

export default function seoPlugin(): Plugin {
  return {
    name: 'hyprism-seo',
    async postBuild({ outDir, siteConfig }) {
      const siteBaseUrl = new URL(siteConfig.baseUrl, siteConfig.url)
      const sitemapUrl = new URL('sitemap.xml', siteBaseUrl).toString()
      const robots = [
        'User-agent: *',
        `Allow: ${siteConfig.baseUrl}`,
        '',
        `Sitemap: ${sitemapUrl}`,
        ''
      ].join('\n')

      await fs.writeFile(path.join(outDir, 'robots.txt'), robots, 'utf8')
    }
  }
}
