# SignPath qualification plan

**Goal:** get [SignPath Foundation](https://signpath.org/) to accept this repo for a free OV Authenticode cert, then sign the Windows exe from GitHub Actions.

**Status:** Phase 1 in progress on `feature/code-signning-prep`. Not ready to apply until that work is on `main` and at least one CI-built GitHub Release exists.

The repo already meets most *policy text* requirements. Reviewers still bounce **verifiable CI origin** (until the new workflow has produced a Release) and **project reputation**.

Apply at [signpath.org/apply.html](https://signpath.org/apply.html) only after Phase 1 is on `main` and at least one CI-built GitHub Release exists.

Terms: [signpath.org/terms.html](https://signpath.org/terms.html)

---

## Why this, not Linux, next

SignPath is the right next step.

- Windows is the only published, used build. SmartScreen is the friction users already hit.
- A signed exe helps every current Nexus download. An untested Linux binary helps nobody until someone can run it.
- SignPath will not sign a locally published `deploy.ps1` exe. They sign the output of a public GitHub Actions job on GitHub-hosted runners. That CI work is also the cheapest way to compile `linux-x64` later without owning a Linux box.
- Linux / Steam Deck stays a later TODO: CI can prove the publish succeeds; it cannot prove Proton + Avalonia + Steam paths work. Borrow a Deck, use a VM, or ask a user when you want that test.

Do not apply the same week as first public traction. A 2026 SignPath decline for another young desktop app cited missing stars, forks, contributors, third-party mentions, and sustained engagement — explicitly *not* a quality judgment. Review takes 1–4 weeks. They look at GitHub first.

155 Nexus downloads and zero complaints in under a day is real usage. It is not yet “a Google search clearly identifies an established project.” Use the next couple of weeks to make the repo look like the project they would be comfortable putting their name on.

---

## Eligibility scorecard (2026-08-22)

### Already in good shape

| Requirement | Where it stands |
| --- | --- |
| OSI license, no dual-license | MIT at repo root |
| Public source | https://github.com/mattdavida/ue4ss-Installer |
| No proprietary code | All project code is in this repo |
| Released in the form to be signed | Windows exe on [Nexus](https://www.nexusmods.com/mortalshell2/mods/96) |
| Documented | README + `nexus.txt` |
| Actively maintained | Commits and PRs on day one |
| Code signing policy on the home page | README section already uses the required heading and wording |
| Team roles | Single maintainer: [mattdavida](https://github.com/mattdavida) as author / reviewer / approver |
| Privacy sentence | README already has the SignPath-required “will not transfer…” line |
| Uninstall | In-app uninstall for UE4SS and tracked mods |
| User-requested network only | Installer talks to GitHub only when the user installs UE4SS or a known signature pack |
| Tests | `dotnet test` suite exists |

### Must fix before applying

| Gap | Why it matters | This branch |
| --- | --- | --- |
| No GitHub Actions | SignPath origin verification requires the signed bytes to come from a GitHub-hosted workflow, not `deploy.ps1` on a home machine | Workflow added; first public Release still needed |
| No GitHub Releases | Reviewers want a first-party download URL they can audit. Nexus is fine as a mirror; it is a weak *only* download page | Tag `v1.0.6` on `main` after the version bump merges. Keep `1.0.0`–`1.0.5` as history. |
| No `Version` / `FileVersion` in the exe | SignPath artifact configs enforce product name + version on the PE | Set to `1.0.6` in csproj + `app.manifest` |
| GitHub repo looks empty | 0 stars, 0 forks, no topics, no Releases tab. That is what they open first | Still a you-on-GitHub task (About / topics / 2FA) |
| MFA on GitHub | Required for every team member. Confirm 2FA is on before you apply | Confirm in GitHub account settings |
| “No hacking tools” framing | UE4SS loads via `dwmapi.dll`. Reviewers who search the name may treat this as security circumvention unless the application says, plainly, that *this* binary only copies files the user asked for | Stated in `CODE_SIGNING.md` and `SECURITY.md` |

### Soft / timing risk (do not ignore)

| Risk | What to do |
| --- | --- |
| Reputation | Ship a CI GitHub Release, keep Nexus numbers climbing, let a short public history exist, then apply |
| Single maintainer | Allowed. Keep roles listed. Review every external PR. Do not force-push `main` |
| Signing UE4SS itself | Never. Sign only `UE4SSInstaller.exe`. Upstream zips stay unsigned and are downloaded at runtime |
| SmartScreen after signing | An OV SignPath cert stops the “unknown publisher” wall. A brand-new hash can still show “less common download” until that *signed* file is downloaded enough times |

---

## What this project is, for SignPath

Use this framing everywhere: README, Nexus, application form.

**UE4SS Installer is a desktop file helper.** It lists local Steam Unreal games, then — only when the user clicks Install — downloads the official UE4SS `experimental-latest` zip from GitHub and extracts it into the `Binaries/Win64` folder the user chose. It can also extract a user-supplied mod zip and a few known community Lua signature packs.

It does **not** inject into a running process, hook APIs, scan for vulnerabilities, disable antivirus, change Windows security settings, or ship UE4SS inside the signed exe.

UE4SS is a separate upstream project ([UE4SS-RE/RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)). This certificate would cover only the installer.

That distinction is the whole “no hacking tools” defense. Do not describe the app as a trainer, injector, bypass, or exploit helper.

---

## Phase 1 — Make the default branch look signable

Do this on `main` before the form. Reviewers will not wait for a feature branch.

### 1. Repo hygiene (same day)

- GitHub About: description matching the README first paragraph, website = Nexus download page, topics such as `ue4ss`, `unreal-engine`, `avalonia`, `modding`.
- Confirm GitHub account 2FA is enabled.
- Keep Issues enabled. A “SmartScreen / virus false positive” issue template is useful; it shows you expect those reports.
- Optional but cheap: `SECURITY.md` (how to report a problem) and `CODE_OF_CONDUCT.md` (Contributor Covenant). Not required. Makes the repo look like a maintained OSS project.

### 2. Finish the code signing policy

README already has the required heading and sentences. Keep that section. Add a short `CODE_SIGNING.md` linked from it so the policy is a dedicated page, and fill the roles SignPath asks for:

- Authors: [mattdavida](https://github.com/mattdavida)
- Reviewers: same; every external PR is reviewed before merge
- Approvers: same; every SignPath signing request is approved by hand
- Attribution: `Free code signing provided by SignPath.io, certificate by SignPath Foundation`
- Privacy: keep the existing “will not transfer…” sentence, and mention GitHub downloads only on user action
- What is signed: `UE4SSInstaller.exe` from this repo only
- What is not signed: UE4SS zips, signature-pack zips, user mod zips
- Linux: unsigned; users should take the official GitHub Release only

Until acceptance, the policy should say **pending**, not “we are signed.”

Mirror a one-line “Code signing policy” link on the Nexus description (`nexus.txt`) and on every GitHub Release body. SignPath wants that phrase on the download page, not only in the repo.

### 3. Put real version metadata on the PE

In `UE4SSInstaller.csproj` (and keep `app.manifest` in sync):

- `Version` / `FileVersion` / `InformationalVersion` — one value per release, e.g. `1.0.6` (no `v` in the PE)
- `Product` = `UE4SS Installer` (already set)
- `AssemblyTitle` = `UE4SS Installer` (already set)
- `Company` = `UE4SS Installer` (project name, not a fake company)
- `Copyright` = `Copyright (c) 2026 Matthew Arvidson`

SignPath will reject a signing request if these do not match the artifact configuration.

### 4. GitHub Actions: test + unsigned Windows publish

Add `.github/workflows/ci.yml` (names can vary):

**On PR / push to `main`**

- `windows-latest` only for the path that matters
- `dotnet test`
- `dotnet publish -c Release -r win-x64 --self-contained true`
- Upload `UE4SSInstaller.exe` as a workflow artifact (unsigned)

**On tag `v*`**

- Same test + publish
- Write `SHA256SUMS.txt` for the exe
- Create a GitHub Release with the exe + checksums
- Release notes: SmartScreen note, VirusTotal if you scanned that build, link to `CODE_SIGNING.md`, “not signed yet”

Do **not** wire the SignPath action until they accept the project. A signing step that fails on missing secrets just looks broken.

Use GitHub-hosted runners only. Self-hosted runners are disallowed for OSS SignPath origin verification.

Optional extra job, not a Phase 1 requirement: `ubuntu-latest` `dotnet publish -r linux-x64` so you know the Linux compile still works. That is not a Linux test.

### 5. Stop treating `deploy.ps1` as the official release

Keep the script for local iteration. The file users download must come from the tag workflow.

After the first CI release, Nexus should get that same exe (same hash), not a different local publish.

### 6. Branch rules (lightweight)

On `main`:

- No force push
- Do not commit straight over unsigned release tags
- Review external PRs (already the policy)

A formal required-review ruleset is awkward for a solo repo. Do not block yourself. The written policy is enough for a one-person project.

---

## Phase 2 — Short public track record

Run in parallel with Phase 1. Do not skip it.

- Publish **v1.0.6** from CI (continues from the existing `1.0.5` local release). Leave older tags in place.
- Upload that exact file to Nexus. One hash, two URLs.
- Leave the VirusTotal link in the README; refresh it when the hash changes.
- Keep a private note of Nexus unique downloads and “no malware / no complaints.” You will paste that into the application.
- Do not spam for stars. Natural Nexus growth is the better signal. If someone on the UE4SS Discord or a game thread already wants the tool, a single honest link is enough.

**Apply when all of these are true:**

1. Phase 1 is on `main`
2. At least one CI-built GitHub Release is public
3. GitHub 2FA is on
4. You have more than a couple of days of clean public history (a week or two is safer than 24 hours)
5. Nexus downloads are still climbing and still complaint-free

If review takes three weeks, those extra days of history happen *during* review only if you already have CI + a Release when you submit. Applying with an empty Releases tab asks them to take Nexus’s word for it.

---

## Phase 3 — Application

Form: https://signpath.org/apply.html

Communication is email. Use an address you check.

### Draft answers

**Project name:** UE4SS Installer

**Repository:** https://github.com/mattdavida/ue4ss-Installer

**License:** MIT (`LICENSE`)

**Download / release URL:**  
https://github.com/mattdavida/ue4ss-Installer/releases  
Mirror: https://www.nexusmods.com/mortalshell2/mods/96

**Description (paste):**

> UE4SS Installer is a small Avalonia desktop app (Windows; a Linux publish exists but is not the signed artifact). Users pick a local Steam Unreal game, choose the official UE4SS Release or zDev channel, and the app downloads that zip from the UE4SS-RE/RE-UE4SS `experimental-latest` GitHub release and extracts it into `Binaries/Win64`. Optional: extract a user-selected community mod zip, or a known signature-pack zip from a listed GitHub repo.
>
> The signed artifact will be only `UE4SSInstaller.exe`, built by GitHub Actions on `windows-latest` from this public repository. The installer does not bundle UE4SS, does not inject into processes, and does not change Windows security settings. Network access is GitHub HTTPS, and only when the user starts an install.
>
> Current public distribution is the Windows exe on Nexus Mods (community installer page) and GitHub Releases. The project is MIT-licensed, actively maintained by a single GitHub user with 2FA, and has a published code signing policy.

**Reputation (paste, update the numbers):**

> Public Windows build has been on Nexus Mods since [date], with [N] unique downloads and no malware or abuse reports as of [date]. Source, tests, and the CI publish workflow are on GitHub. This is a new project; the installer is a convenience wrapper around the existing UE4SS release zips, which the community already distributes unsigned.

**Build / signing intent:**

> After acceptance: tag `v*` → GitHub Actions publish → upload unsigned exe as a workflow artifact → `signpath/github-action-submit-signing-request` → manual approval by mattdavida → attach the signed exe to the GitHub Release. Test-certificate policy first, then release certificate.

Do not argue policy if they decline. Their terms say they will not debate it. Reapply later with more history.

---

## Phase 4 — After acceptance

SignPath’s name is on the cert. They will require their constraints.

1. Create the SignPath.org project; install the SignPath GitHub App on this repo.
2. Trusted build system: GitHub.com, this repository, the release workflow, branch/tag `refs/tags/v*`, GitHub-hosted runners only.
3. Artifact configuration for a single PE:
   - path `UE4SSInstaller.exe`
   - `product-name` = `UE4SS Installer`
   - `product-version` / `file-version` = the same `${version}` you pass from the workflow
4. Two signing policies if they offer them: test cert for dry runs, release cert for tags. Every release signing request is **manually approved**.
5. Add repo secrets `SIGNPATH_API_TOKEN` and `SIGNPATH_ORG_ID`. Then add the submit step *after* `actions/upload-artifact`, using that artifact id.
6. Workflow: wait for signing → download signed exe → `signtool verify /pa` on the Windows runner → publish that file (not the unsigned one) to the GitHub Release.
7. Upload the signed file to Nexus. Update VirusTotal, README SmartScreen section, and `nexus.txt`. Signed does not mean “never warn again” on the first hash.
8. Do not sign UE4SS DLLs, `dwmapi.dll`, or anything downloaded at runtime.

If they ask for an SBOM later, add a `dotnet` SBOM step then. Do not invent one now.

---

## Phase 5 — If they decline

Typical reason: “not enough reputation,” not “your code is bad.”

- Keep shipping unsigned from CI + checksums + the existing SmartScreen README.
- Reapply after more downloads, a few GitHub stars or issues, and a longer release history.
- Paid fallback if SmartScreen becomes a support problem and you are in a supported region: Azure Trusted Signing (identity + small monthly fee, no OSS reputation bar). Only worth it if SignPath is a hard no and Windows users are bouncing.

---

## Deferred: Linux / Steam Deck

Leave this off the SignPath critical path.

| Later | Why it waits |
| --- | --- |
| `ubuntu-latest` publish of `linux-x64` | Cheap compile check; add after Windows CI is boring |
| Real Steam Deck / Proton run | Needs hardware or a volunteer. Installer paths assume `Binaries/Win64` under Proton |
| Linux signing | Not SignPath’s job for this app. Cosign/GPG can come after Windows is signed |

README already says the Linux build exists and is less tested. Keep saying that until someone actually runs it.

---

## Suggested order of work

1. Confirm GitHub 2FA.
2. ~~csproj version + company metadata.~~
3. ~~`CODE_SIGNING.md` + README / `nexus.txt` links.~~
4. ~~CI workflow: test, win-x64 publish, tag → GitHub Release + SHA256.~~
5. Cut `v1.0.6` from `main`, upload that hash to Nexus. Do not tag `0.1.0` or reuse `1.0.5`.
6. Repo About / topics (`SECURITY.md` is in this branch).
7. Wait until the Release has been public a bit and Nexus is still clean.
8. Submit https://signpath.org/apply.html with the draft above.
9. After yes: SignPath app, artifact config, gated signing step, signed Nexus upload.

---

## Out of scope for this plan

- Paying for a personal OV/EV cert
- Signing UE4SS or any game files
- Changing installer behavior to look “safer” (do not strip uninstall or GitHub downloads)
- A website other than GitHub + Nexus
- Auto-approval of production signing
