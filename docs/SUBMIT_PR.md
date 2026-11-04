# Submitting this branch

The local checkout is based on the real upstream history; no upstream write access or published fork is assumed. Branch: `modernization/native-lunaris-1.9.0`. The configured origin is the official repository. Commit authorship uses the local Codex contributor identity; review it and configure your preferred Git identity before publishing.

1. Review `CHANGELOG.md`, `CHANGE_INVENTORY.md`, `VALIDATION.md` and the code diff against upstream 1.8.4. Complete the game matrix, or clearly mark the PR as a draft with those items pending.
2. Fork the official repository into your GitHub account through GitHub.
3. In this checkout, add your fork as a separate remote. Replace YOUR_USER below; do not push to the official origin.

```sh
git remote add fork https://github.com/YOUR_USER/EverShor-Erenshor-Classic-Skills.git
git push -u fork modernization/native-lunaris-1.9.0
```

4. On GitHub open a pull request: base `krautchanpro:main`, compare your fork's branch. Copy the title/body from `PR_DESCRIPTION.md`. Prefer a draft while runtime checks remain pending.
5. Do not upload game DLLs, private data or lib/bin/obj directories. A source ZIP can be produced using `git archive`; a binary is a separate review/testing artifact, not a source commit.

A source-only ZIP is provided separately for inspection. The prepared Git checkout is the recommended way to preserve upstream ancestry when creating the PR.
