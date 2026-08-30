# Repository working rules

## Version and revision management (mandatory)

- Before delivering any changed application or build-tool code, increment `DevelopmentRevision` in `Directory.Build.props`. The current version is defined by that file, never by a folder name or an old document. Read its current value; do not hardcode the revision from this instruction.
- `ApplicationVersionPrefix` and `DevelopmentRevision` are the only editable version inputs. Derive `Version`/`InformationalVersion` as `<prefix>-dev.<revision>` and `AssemblyVersion`/`FileVersion` as `<prefix>.<revision>` for every solution project. Never override these values in a project, on the command line, in UI strings or in a distribution label.
- Same-source verification rebuilds may retain their revision, but must use a new timestamped output folder. Changed build inputs must not reuse a previously certified revision. Do not automatically increment on each compile or alter old build folders to bypass the check.
- Update the current snapshot in `IMPLEMENTATION_STATUS.md`, the current README version and the relevant release/verification notes. Preserve historical revision numbers in old results. Project `formatVersion` and `minimumApplicationVersion` describe data compatibility: do not bump them merely because the application revision changed.
- Build from the repository root. Run Release solution build, contract tests, `tools/TestVersioning.ps1` and relevant app diagnostics. Use `tools/BuildPortable.ps1` to deliver. It checks version consistency, source-fingerprint reuse and published binary metadata, and records `build-info.json`.
- Verify `tools/GetBuildVersion.ps1 -PublishDirectory <exact-new-build-directory>` before handoff. Confirm the title/About display, four-part executable version, project-manifest application version and distribution folder label agree. Report the exact revision and absolute build location.
- Never claim verification or Git tracking that did not succeed. A missing/unusable Git repository must be reported, not silently reinitialized. Do not commit, push, tag or modify Git history unless requested.

See [VERSIONING.md](VERSIONING.md) for the operational policy and limitations of the local publication checks.
