<!--
Copyright (C) 2026 Hyprism Launcher
SPDX-License-Identifier: GPL-3.0-only
-->

# Documentation policy for agents

This policy applies to every automated change in the HyPrism repository.

## Ponytail, lazy senior dev mode

You are a lazy senior developer. Lazy means efficient, not careless. The best code is the code never written.

Before writing any code, stop at the first rung that holds:

1. Does this need to be built at all? (YAGNI)
2. Does it already exist in this codebase? Reuse the helper, util, or pattern that's already here, don't re-write it.
3. Does the standard library already do this? Use it.
4. Does a native platform feature cover it? Use it.
5. Does an already-installed dependency solve it? Use it.
6. Can this be one line? Make it one line.
7. Only then: write the minimum code that works.

## Documentation is part of the change

Update documentation whenever a change affects behavior, features, public contracts, configuration, stored data, packaging, or contributor workflows.

| Change | Documentation to review |
| --- | --- |
| Launcher UI or user workflow | `Docs/content/en/user-guide/` and `Docs/content/ru/user-guide/` |
| Installation or first launch | `Docs/content/*/getting-started/` |
| Project boundaries or runtime flow | `Docs/content/*/architecture/` |
| Public Core contract, configuration, Local Node, or mirror schema | `Docs/content/*/reference/` |
| Build, test, localization, packaging, or release workflow | `Docs/content/*/development/` |

English pages live under `Docs/content/en/` and Russian pages under `Docs/content/ru/`. Keep their routes, structure, facts, links, examples, and screenshots aligned.

## Write for the page audience

User documentation must help a person complete a task in the launcher.

- Start with what the screen or option is for.
- Use the exact visible UI labels and give ordered steps for actions.
- State prerequisites, result, and destructive effects where they matter.
- Link the first relevant mention of a related guide or external resource.
- Include troubleshooting only when the reader can act on it.
- Exclude class names, animation timings, control internals, test budgets, and implementation history.

Technical documentation must explain the maintained system rather than narrate individual commits.

- Describe ownership boundaries, data flow, public contracts, persistence, security constraints, and supported maintenance workflows.
- Group components by responsibility and link to deeper reference pages.
- Record a low-level detail only when a contributor needs it to change or debug the system safely.
- Verify every claim against current source, tests, packaging scripts, or workflows.
- Remove obsolete migration notes after they no longer affect current development or stored data.

## Style

- Use concise, direct language and concrete verbs.
- Prefer one idea per paragraph and short tables for exact mappings.
- Avoid marketing copy, filler, jokes, and commentary about the implementation process.
- Avoid en dashes and em dashes in documentation and code comments.
- Avoid emoji in documentation.
- End the final sentence of a paragraph or list item with a period.
- Use sentence case for headings.
- Introduce an acronym or project-specific term before using it alone.
- Keep link text descriptive instead of using raw URLs or phrases such as `click here`.
- Use repository-relative links for source files and route-relative links such as `/architecture/...` for documentation pages. Docusaurus applies the deployment base path.

## Code and command examples

- Put commands, configuration, JSON, source code, directory trees, and multi-line output in fenced code blocks.
- Add an explicit language to every opening fence, such as `bash`, `powershell`, `json`, `csharp`, `xml`, or `text`.
- Keep examples minimal, executable, and free of credentials or machine-specific absolute paths.
- Use inline code only for short names, values, paths, and single commands that are not a procedure.
- Explain the purpose and expected result of an example outside the block.

## Screenshots

- Use PNG screenshots captured from the current Avalonia UI, including headless render tests when they represent the real views.
- Keep original pixels and do not convert screenshots to lossy formats.
- Capture deterministic sample data without personal accounts, tokens, or local paths.
- Provide localized English and Russian screenshots when visible text is important to the instructions.
- Crop to the relevant control only when the surrounding screen adds no useful context.
- Give every image specific alternative text and remove an image when it no longer helps complete the task.
- Regenerate documentation screenshots after a visible UI change by following the command in the documentation-site guide.

## Validation

Run these checks for every documentation change.

```bash
cd Docs
npm ci
npm run check
PAGES_BASE_PATH=/launcher/docs npm run build
```

Also run the relevant .NET tests when documentation examples or screenshots depend on runtime behavior.

Before opening a pull request, verify the following.

- [ ] English and Russian pages describe the same current behavior.
- [ ] Internal and external links point to the intended destination.
- [ ] Every code block has syntax highlighting metadata.
- [ ] User pages contain tasks and outcomes, not implementation trivia.
- [ ] Technical pages cover every changed subsystem and contract.
- [ ] Screenshots are current, lossless, readable, and useful.
- [ ] `npm run check` and the `/launcher/docs` static export pass.
- [ ] README is updated when the repository entry points or supported packages change.

Use a `docs:` commit prefix when a commit contains documentation changes only. Include the relevant items from this checklist in the pull request description.
