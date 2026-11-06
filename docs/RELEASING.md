# Versions, branches and releases

How Trackarr is versioned and released. You decide when something is a release by pushing a version tag. Nothing
else publishes an image that users get.

## Versions

Plain semantic versions: **MAJOR.MINOR.PATCH**, such as `6.0.1`. No `-alpha` or `-beta` suffixes. Tags up to
`v6.0.0-alpha` used them; they stay as history and the release workflow ignores them.

| Bump | When | Example |
|------|------|---------|
| Patch | Fixes only. Nothing for users to do | 6.0.0 → 6.0.1 |
| Minor | New features. Upgrading needs no action from users | 6.0.1 → 6.1.0 |
| Major | Users have to do something, or can't go back: a database change that can't be downgraded, login added, a setting or port removed | 6.1.0 → 7.0.0 |

The version lives in one place: `<Version>` in [Trackarr.csproj](../Trackarr/Trackarr.csproj). The Status page and
the update check in the top bar read it. A release fails if the tag and this value don't match.

The next release should be **6.0.1** or higher. The app's update check compares only the numbers, so `6.0.0` would
look the same as the `6.0.0-alpha` people already run.

## Branches

| Branch | What it holds | Merges into |
|--------|---------------|-------------|
| `master` | Released code. Every release is a tag on `master` | — |
| `develop` | Work for the next release. Must build and pass tests | `master`, when you release |
| `v7/<phase>-<topic>` | One change, such as `v7/phase2-arr-client` ([ROADMAP](ROADMAP.md)) | `develop`, through a PR |
| `hotfix/<topic>` | A fix for the released version, or CI and docs changes both branches need | `master`, then `master` into `develop` |

PRs into `develop` can be squashed. **PRs between `master` and `develop` (either way) use "Create a merge
commit".** Squashing them makes git treat the two branches as unrelated work, and every later sync conflicts.

`develop` is protected: changes go through a PR, the `test` check must pass, and force pushes are blocked. This
applies to admins too.

## What gets published

| Event | Runs | Publishes |
|-------|------|-----------|
| PR into `master` or `develop` | Tests ([ci.yml](../.github/workflows/ci.yml)) | Nothing |
| Push to `master` or `develop` (a merged PR) | Tests | Nothing |
| Tag `vX.Y.Z` on `master` | Checks, tests, image, GitHub release ([release.yml](../.github/workflows/release.yml)) | Docker `X.Y.Z` and `X`, `latest` if it's the newest release, and a GitHub release |
| Actions > Release > Run workflow, on a branch | Tests, image | Docker `:<branch>` only, such as `:develop` |

Docker tags users can choose:

| Tag | Gets | For |
|-----|------|-----|
| `latest` | The newest release | Most people. The Unraid template uses it |
| `6` | The newest 6.x release | Staying on v6 after v7 is released |
| `6.0.1` | Exactly that release | Pinning a version |
| `develop` | Whatever you last built by hand | Your own testing. Can break a database |

## Releasing from develop

1. In the last PR into `develop` (or a small PR of its own), set `<Version>` in `Trackarr.csproj` and add
   `docs/releases/vX.Y.Z.md`. That file becomes the GitHub release notes and the in-app Releases page. Follow
   [v6.0.0-alpha](releases/v6.0.0-alpha.md): "Before You Upgrade" first, then what's new and what's fixed.
2. Open a PR from `develop` into `master` named "Release vX.Y.Z" and merge it with a merge commit.
3. Tag `master` and push the tag:

   ```bash
   git fetch origin
   git tag v6.1.0 origin/master
   git push origin v6.1.0
   ```

4. Watch Actions > Release. When it's green, the image and the GitHub release are live, and Unraid shows the update.

Push the tag yourself rather than using GitHub's "Draft a new release" button. The workflow writes the release.

## Fixing a release (hotfix)

When `develop` has work that isn't ready but users need a fix now:

1. `git switch -c hotfix/<topic> origin/master`. Fix it, bump the patch version and add `docs/releases/vX.Y.Z.md`.
2. PR into `master` and merge.
3. Tag and push as above.
4. Open a PR from `master` into `develop` and merge it with a merge commit, so `develop` gets the fix. If
   `<Version>` conflicts, keep `develop`'s.

## When a release fails

The `check` job says what's wrong: a malformed tag, a version mismatch, missing release notes, or a tag that isn't on
`master`. Nothing has been published at that point. Delete the tag, fix the problem, and tag again:

```bash
git tag -d v6.1.0
git push origin :refs/tags/v6.1.0
```

If a later job fails (Docker Hub was down, for example), use "Re-run failed jobs" on the run.

Never move a tag that already published an image. People may have pulled it. Release the next patch instead.

## Test images

Actions > Release > Run workflow, then pick a branch. This publishes `confinedfish/trackarr:<branch>` (`develop`, or
`v7-phase0-prep` for `v7/phase0-prep`) and nothing else. The version shown in the app is whatever
`Trackarr.csproj` says, so it isn't a release. Delete branch images on Docker Hub when you're done with them.

## Before v7

v7 changes the database and adds login, so it's a major version. People on `latest` get it automatically, so its
release notes need a "Before You Upgrade" section that says to back up `/config` (or that Trackarr does it, once F13
lands) and to use the `6` tag to stay on v6.

## Leftovers from the old setup

- Git tags `v.5.0.10-alpha`, `5.0.10-alpha`, `v3.0.0-Alpha`, `alpha` and `latest` are malformed. The workflow ignores
  them. They can be renamed or deleted at any time.
- Docker Hub has commit tags (`3e2f07b`, `8c84ac5` and others) and an `alpha` tag that won't be updated again. Delete
  them on Docker Hub (Tags, select, Delete). Keep the version tags (`6.0.0-alpha`, `5.1.0-alpha` and so on) for anyone
  pinned to them.
