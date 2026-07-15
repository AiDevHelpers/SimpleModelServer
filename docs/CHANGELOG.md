# Changelog

All notable changes to this project are documented here. Format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow the scheme resolved
by Nerdbank.GitVersioning from `version.json`.

## [Unreleased]

### Added

- NuGet packaging for `SimpleModelServer.Config` and `SimpleModelServer.Library` (`dotnet pack`, output to
  `build/dist/<config>/nuget/`).
- MSI installer packaging for `SimpleModelServer.Loader` via WiX (`build/wix/SimpleModelServer.Setup`),
  output to `build/dist/<config>/SimpleModelServer-Setup-<version>.msi`.
- `version.json` for Nerdbank.GitVersioning.
- `.gitignore`, `README.md`.
