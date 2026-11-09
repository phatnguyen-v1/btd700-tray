# Code signing policy

Free code signing provided by [SignPath.io](https://about.signpath.io), certificate by [SignPath Foundation](https://signpath.org)

## How releases are built and signed

- `Btd700Tray.exe` is built from this repository's source by the [build workflow](.github/workflows/build.yml) on a
  GitHub-hosted Windows runner, never on a personal PC.
- Only builds of a release tag (`v*`) are submitted for signing, and every signing request is approved by hand.
- Signed files are attached to the matching [GitHub release](../../releases).

## Team roles

- Committers and reviewers: [phatnguyen-v1](https://github.com/phatnguyen-v1)
- Approvers: [phatnguyen-v1](https://github.com/phatnguyen-v1)

Changes from anyone else (pull requests) are reviewed by a committer before they are merged.

## Privacy policy

This program will not transfer any information to other networked systems unless specifically requested by the user
or the person installing or operating it.

It talks only to the BTD 700 dongle over USB. It keeps its settings and a log in `%APPDATA%\Btd700Tray`, and only
changes the system when you ask it to: **Start with Windows** adds or removes `Btd700Tray` under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. To remove it, untick Start with Windows, exit the app, then delete
the exe and `%APPDATA%\Btd700Tray`.
