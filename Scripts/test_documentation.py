# Copyright (C) 2026 Hyprism Launcher
# SPDX-License-Identifier: GPL-3.0-only

"""Regression checks for the documentation validator."""

from pathlib import Path
from tempfile import TemporaryDirectory
import unittest
from unittest.mock import patch

import check_documentation as checks


class DocumentationChecksTests(unittest.TestCase):
    def setUp(self):
        self.directory = TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.root = Path(self.directory.name)
        self.content = self.root / "Docs/content"
        self.patches = patch.multiple(checks, ROOT=self.root, CONTENT=self.content)
        self.patches.start()
        self.addCleanup(self.patches.stop)

    def page(self, name, text):
        path = self.content / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding="utf-8")
        return path

    def prose_errors(self, text):
        path = self.page("en/index.mdx", text)
        with patch.object(checks, "PROSE_FILES", [path]):
            return checks.check_prose()

    def test_language_and_closing_fence_are_required(self):
        self.assertTrue(self.prose_errors("```\nexample\n```"))
        self.assertTrue(self.prose_errors("```json\n{}"))

    def test_fence_metadata_nested_examples_and_tildes(self):
        self.assertFalse(self.prose_errors('```json title="Config"\n{}\n```'))
        self.assertFalse(self.prose_errors("~~~~md\n```json\n{}\n```\n~~~~"))

    def test_examples_do_not_trigger_prose_or_link_checks(self):
        self.assertFalse(self.prose_errors("```text\nA sentence.\n```"))
        self.page("en/index.mdx", "```md\n[Example](/docs/missing)\n![Example](missing.png)\n```")
        self.assertFalse(checks.check_links())
        self.assertFalse(checks.check_images())

    def test_final_periods_are_allowed(self):
        self.assertFalse(self.prose_errors("A complete sentence."))
        self.assertFalse(self.prose_errors("- A complete list item."))

    def test_en_and_em_dashes_are_rejected(self):
        self.assertTrue(self.prose_errors("Do not use an em dash — here."))
        self.assertTrue(self.prose_errors("Do not use an en dash – here."))

    def test_bilingual_anchors_are_checked_in_each_locale(self):
        self.page("en/index.mdx", "[Settings](/settings#downloads)")
        self.page("en/settings.mdx", "## Downloads {/* #downloads */}")
        self.page("ru/index.mdx", "[Настройки](/settings#downloads)")
        self.page("ru/settings.mdx", "## Загрузки")
        self.assertEqual(len(checks.check_links()), 1)
        self.page("ru/settings.mdx", "## Загрузки {/* #downloads */}")
        self.assertFalse(checks.check_links())

    def test_missing_routes_and_repository_targets(self):
        self.page("en/index.mdx", "[Guide](/missing)\n[Source](../../../missing.cs)")
        self.assertEqual(len(checks.check_links()), 2)

    def test_images_need_alternative_text_and_existing_files(self):
        self.page("en/index.mdx", "![](missing.png)")
        self.assertEqual(len(checks.check_images()), 2)

    def test_missing_translation_is_reported(self):
        self.page("en/index.mdx", "# Home")
        self.assertEqual(len(checks.check_language_parity()), 1)


if __name__ == "__main__":
    unittest.main()
