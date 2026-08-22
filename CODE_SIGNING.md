# Code signing policy

**Status: pending.** Windows releases are not signed yet. This project is preparing to apply to [SignPath Foundation](https://signpath.org/) for a free certificate. Do not treat the current exe as signed.

If accepted:

> Free code signing provided by SignPath.io, certificate by SignPath Foundation

Until then, Windows SmartScreen may show **Windows protected your PC** on first run. That is an unsigned-file warning, not a malware verdict. Click **More info**, then **Run anyway**.

## What will be signed

- `UE4SSInstaller.exe` built from this repository and published on [GitHub Releases](https://github.com/mattdavida/ue4ss-Installer/releases).

## What will not be signed

- UE4SS zips from [UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)
- Known signature-pack zips from their own GitHub repos
- Community mod zips the user selects

Those files are downloaded or chosen at the user's request. They are not bundled in the installer and must not be signed with this project's certificate.

## Build and signing process

- Official Windows builds come from GitHub Actions on GitHub-hosted `windows-latest` runners (see `.github/workflows/ci.yml`).
- Only that CI-built `UE4SSInstaller.exe` will be submitted to SignPath.
- The private key is held by SignPath (HSM-backed). This project does not store a code-signing private key.
- Every production signing request will be approved by hand.
- `deploy.ps1` is for local iteration only. Do not upload a local publish as the official download.

## Team roles (single-maintainer project)

- **Authors** (commit access, can modify the repository without additional reviews): [mattdavida](https://github.com/mattdavida)
- **Reviewers** (review required for changes proposed by non-committers, e.g. pull requests): [mattdavida](https://github.com/mattdavida). All external pull requests are reviewed by the maintainer before merge.
- **Approvers** (approve each signing request): [mattdavida](https://github.com/mattdavida). Each signing request requires explicit approval by the maintainer.

All team members must use multi-factor authentication for GitHub (and for SignPath, once the project is accepted).

## Linux

Linux artifacts are not cryptographically signed. If a Linux build is published, take it only from the official GitHub Releases page.

## Distribution

- Official: https://github.com/mattdavida/ue4ss-Installer/releases
- Mirror: https://www.nexusmods.com/mortalshell2/mods/96

After the first tagged CI release, the Nexus file should be the same bytes as the GitHub Release exe.

## Privacy

This program will not transfer any information to other networked systems unless specifically requested by the user or the person installing or operating it. Choosing **Install UE4SS** or a known signature pack downloads files from GitHub at your request.
