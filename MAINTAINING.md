# Maintaining the RockList fork

## Repository roles

- `Jamesllllllllll/RockSnifferLib` is the public source of truth for reusable
  RockList RockSnifferLib changes.
- `PoizenJam/RockSnifferLib` is the active functional upstream used by the
  RockList fork.
- `kokolihapihvi/RockSnifferLib` is the original upstream and license/history
  source.
- RockList Desktop carries a generated vendored snapshot of this repository.

Reusable library changes should be reviewed here before they are included in a
RockList Desktop release. If an urgent change is prototyped in the private
RockList monorepo, port it back here and sync the reviewed public revision into
RockList before the next Desktop release.

## Recommended remotes

```bash
git remote add upstream https://github.com/PoizenJam/RockSnifferLib.git
git remote add original https://github.com/kokolihapihvi/RockSnifferLib.git
git fetch --all --tags
```

Upstream updates should be merged through a dedicated branch and pull request:

```bash
git switch master
git pull --ff-only origin master
git switch -c sync/poizenjam-YYYYMMDD
git merge upstream/master
```

Resolve conflicts in favor of validated behavior rather than automatically
overwriting RockList changes. Run the build workflow before merging.

## Release versions

Until the fork has a stable independent API, use tags such as
`v0.6.9-rocklist.1`:

- The numeric base identifies the PoizenJam-compatible release.
- The `rocklist.N` suffix identifies a RockList-maintained revision.
- Update [CHANGELOG.md](CHANGELOG.md) for every tag.

## Syncing RockList Desktop

After merging and tagging a public library revision, run the sync tool from
this repository:

```bash
node tools/sync-rocklist-vendor.mjs /path/to/rocklist
```

The tool replaces only the RockSnifferLib portion of RockList's vendor tree,
excludes build output and repository metadata, and records the public commit in
RockList's vendor-source file. Review and test the resulting RockList change
before releasing Desktop.

Use `--check` to verify that a RockList checkout matches this repository:

```bash
node tools/sync-rocklist-vendor.mjs --check /path/to/rocklist
```

The sync tool refuses to export an uncommitted library worktree so vendored
Desktop builds always point to a reproducible public revision.
