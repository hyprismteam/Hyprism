// Copyright (C) 2026 Hyprism Launcher
// SPDX-License-Identifier: GPL-3.0-only

import { existsSync, statSync } from 'node:fs'
import path from 'node:path'

type Node = {
  type: string
  url?: string
  children?: Node[]
}

// Keep source links usable in the checkout and on GitHub Pages
export default function repositoryLinks(options: { repositoryRoot: string }) {
  return (tree: Node, file: { path: string }) => {
    function visit(node: Node): void {
      if (node.type === 'link' && node.url?.startsWith('../')) {
        const [target, anchor] = node.url.split('#', 2)
        const absolute = path.resolve(path.dirname(file.path), target)
        const relative = path.relative(options.repositoryRoot, absolute)
        if (!relative.startsWith('..') && !relative.startsWith(`Docs${path.sep}`)) {
          if (!existsSync(absolute)) {
            throw new Error(`Missing repository link in ${file.path}: ${target}`)
          }
          const kind = statSync(absolute).isDirectory() ? 'tree' : 'blob'
          const encoded = relative.split(path.sep).map(encodeURIComponent).join('/')
          node.url = `https://github.com/hyprismteam/Hyprism/${kind}/main/${encoded}${anchor ? `#${anchor}` : ''}`
        }
      }
      node.children?.forEach(visit)
    }
    visit(tree)
  }
}
