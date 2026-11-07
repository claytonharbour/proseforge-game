---
name: workstation-setup
description: Get a reader's computer ready to build, test and run One More Story, or just report what is missing. Checks Git, the .NET 10 SDK, the .NET MAUI workload, Xcode (Mac), and a JDK plus the Android SDK, installs only what the person agrees to, and finishes on a green `make test`. Use when asked to set up, check, or fix a machine for proseforge-game.
---

# Set up a workstation for One More Story

The goal is one result the person can see: **the game's tests pass on their machine.** Everything
below serves that. The book's chapter "Set Up Your Workshop" is the human version of this skill.

## Two modes. Ask which, unless the request already says

- **Check only:** "what's missing?", "is my machine ready?". Run the checks and report. No
  software installs, updates or uninstalls, and no changes to system settings, shell profiles or
  saved environment variables. The only variables set are the two temporary `DOTNET_` ones under
  "Keep .NET's first run quiet" below, for those commands or that terminal session only; say so in
  the report. **This still includes step 3, the tests:** they are the check that matters,
  and they do write files: build output in the game's `bin`/`obj` folders, and packages downloaded
  into the NuGet cache in the person's home folder. Say so in the report. If the person wants their
  game folder left exactly as it was, run the tests in a temporary copy made with
  `git clone <game folder> <temp folder>`, never a plain file copy: a clone takes only the tracked
  files, so the private `.env` and `.env.local` (signing keys and personal settings, which `make`
  reads) stay behind.
- **Set up:** check, then install what's missing, one tool at a time, **asking before each
  install**. Say what you'll run and why, then wait for a yes.

## Ground rules

- **Never install, update or uninstall anything without a yes for that tool.** An installer that
  needs an administrator password (`sudo`, a Windows UAC prompt, Xcode's licence) is the person's to
  approve and type. Tell them the command and let them run it if they prefer.
- **Don't touch what already works.** A tool that passes its check stays as it is, even if a newer
  version exists.
- **Report what you saw, not what you expected.** Quote the command and its real output for every
  check. "Probably installed" is not a result.
- **Stop at anything that costs money or needs an account** (the Apple Developer Program, a Google
  Play account, a paid AI plan). Say what it's for and that it's optional for building and testing.
- **Keep .NET's first run quiet.** Run every `dotnet` and `make` command with
  `DOTNET_GENERATE_ASPNET_CERTIFICATE=false` and `DOTNET_NOLOGO=1` set for those commands only
  (`DOTNET_NOLOGO=1 DOTNET_GENERATE_ASPNET_CERTIFICATE=false make test` on a Mac; in PowerShell, set
  `$env:DOTNET_NOLOGO` and `$env:DOTNET_GENERATE_ASPNET_CERTIFICATE` in that session). Never save them
  to a profile. The first
  `dotnet` command on a machine otherwise installs a web development certificate the game doesn't
  need.
- Work from the top of the game's folder (the one with the `Makefile` and the `themes` folder).

## 1. Find out where you are

- **Mac or Linux:** `uname -s` (macOS prints `Darwin`, Linux prints `Linux`) and `uname -m` for the
  chip (`arm64` or `x86_64`).
- **Windows**, in PowerShell (Windows PowerShell 5.1 or PowerShell 7):
  `[Environment]::OSVersion.VersionString` for the OS, and `$env:PROCESSOR_ARCHITECTURE` for the chip
  (`AMD64` or `ARM64`).

The rest depends on it: Xcode and iPhone builds exist only on a Mac. On Windows, every command below
is PowerShell, so environment variables are written `$env:NAME`, never CMD's `%NAME%`.

## 2. The checks, in order

Do them in this order. Each later step depends on the earlier ones.

| # | Tool | Check | Passes when |
|---|---|---|---|
| 1 | Git | `git --version` | prints a version |
| 2 | .NET 10 SDK | `dotnet --list-sdks` | a line starts with `10.` |
| 3 | The game builds and its tests pass | `make test` (Mac/Linux), or on Windows `dotnet run --project tests/ProseForge.Game.Rules.Tests -c Release` then `dotnet run --project tools/ThemeCheck -c Release -- .` | every area prints `PASS:`, every theme prints `ok`, exit code 0 |
| 4 | MAUI workload | `dotnet workload list` | the list includes `maui` |
| 5 | Xcode (Mac only) | `xcodebuild -version` | prints `Xcode` and a version, not a command-line-tools error |
| 6 | JDK 21 (Android) | `java -version` | version 21 |
| 7 | Android SDK | `$ANDROID_HOME`, or `~/Library/Android/sdk` (Mac), `$env:LOCALAPPDATA\Android\Sdk` (Windows), contains `platform-tools/adb` | the folder and `adb` exist |

**Step 3 is the first green, and it needs only steps 1 and 2.** If the person only wants to follow
the book, a pass there is enough to stop and celebrate. Steps 4 to 7 are for running the app.

## 3. Installing what's missing (set-up mode only)

Ask before each one. These are the routes the book uses; prefer them so the person's machine matches
the chapter.

- **Git:** Mac: `xcode-select --install` (it brings Git). Windows: Git for Windows from
  git-scm.com. Linux: the distribution's package manager.
- **.NET 10 SDK:** the installer from dotnet.microsoft.com/download (choose .NET 10). Open a **new**
  terminal afterwards, or `dotnet` won't be found.
- **MAUI workload:** `sudo dotnet workload install maui` on a Mac, `dotnet workload install maui` on
  Windows. On a Mac the person types the password.
- **Xcode:** from the Mac App Store. Then open it once to accept its licence. If a build later
  complains about the Xcode version, the MAUI release-versions table on GitHub (dotnet/maui wiki)
  says which one it wants.
- **JDK 21:** Microsoft's build of OpenJDK (learn.microsoft.com/java/openjdk/download).
- **Android SDK:** let .NET fetch it, from the game's folder:
  `dotnet build src/ProseForge.Game/ProseForge.Game.csproj -f net10.0-android -t:InstallAndroidDependencies -p:AndroidSdkDirectory="$HOME/Library/Android/sdk" -p:AcceptAndroidSDKLicenses=True`
  (Windows PowerShell: `-p:AndroidSdkDirectory="$env:LOCALAPPDATA\Android\Sdk"`). This accepts Google's SDK
  licences, so say that before running it.

After each install, **re-run that tool's check** and quote the new output.

If the tools live somewhere unusual, the person can say so in `.env.local` (copy
`env.local.example`): `ANDROID_SDK` and `JDK`. Don't edit their shell profile.

## 4. Prove it

Finish with step 3's tests, then, if the person wants to see the game:

- **Mac:** `make run-mac` opens it in a phone-shaped window. No Apple account needed.
- **Android phone** (USB debugging on) or a running emulator:
  `dotnet build src/ProseForge.Game/ProseForge.Game.csproj -f net10.0-android -t:Run`

## 5. Report

End with a short table, one row per tool: **present / installed now / missing / skipped**, and the
version you saw. Then one line on what they can do now ("the tests pass; you can follow every
exercise"), and anything left for them, such as an install that needed their password.

## When something fails

Quote the error, then check the usual causes before guessing: a terminal opened before an install
(open a new one), the wrong .NET major version (`dotnet --list-sdks`), Xcode installed but never
opened, or `ANDROID_HOME` pointing at an empty folder. If it still fails, say exactly where you
stopped and what you'd try next. Don't uninstall or reinstall anything to get past it without asking.
