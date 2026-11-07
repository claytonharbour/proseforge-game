# Releasing the game

How to build a signed release and put it on the stores' **testing** tracks. Nothing here publishes
to the public stores: uploads go to Google Play's internal testing track (as a draft) and to
TestFlight.

## One-time setup

### Your app's identity: `.env.local`

Copy `env.local.example` to `.env.local` and set at least `APP_ID`, your app's ID in reverse-domain
style (`com.yourname.mygame`). Both stores fix the ID on the first upload, so choose it once. The
same file sets the name under the icon, where your Android SDK and JDK are, and where your App Store
Connect key file lives. Git ignores `.env.local`.

### Store accounts

- **Google Play Console:** create the app with your `APP_ID` as its package name, and turn on Play App
  Signing. Give a Google Cloud service account access to the app (Play Console → Users and
  permissions) and download its JSON key. The Play API can't create an app, and Play needs the first
  bundle uploaded by hand in the console; after that, `make upload-android` works.
- **Apple:** register your `APP_ID` as a bundle ID, create the app record in App Store Connect, and
  make an App Store distribution provisioning profile for it. For `make asc-status`, create an App
  Store Connect API key and download its `.p8` file.

### Secrets: `.env`

Git ignores `.env` too. Never commit it.

```sh
# Android upload key: a keystore kept outside the repo. Back it up; a lost key is hard to replace.
ANDROID_KEYSTORE_PATH=/absolute/path/to/my-game-upload.keystore
ANDROID_KEY_ALIAS=upload
ANDROID_KEYSTORE_PASSWORD=...
ANDROID_KEY_PASSWORD=...

# Play publishing: the service account's JSON key, kept outside the repo
GOOGLE_PLAY_SERVICE_ACCOUNT_JSON=/absolute/path/to/service-account.json

# iOS signing
IOS_CODESIGN_KEY=Apple Distribution: <Name> (<TEAM_ID>)
IOS_PROVISIONING_PROFILE=<profile name>

# App Store Connect: upload, and status through the API key
APPLE_ID=<apple id email>
AC_PASSWORD=...   # an app-specific password from account.apple.com
APPLE_KEY_ID=<key id>
APPLE_UUID=<issuer id>
```

To make the Android upload key:

```sh
keytool -genkeypair -v -keystore my-game-upload.keystore -alias upload -keyalg RSA -keysize 2048 -validity 10000
```

`make` reads both files and hands each tool only the variable it needs, on its own command line.
Don't `export` these into a shell: another process on the machine can read your environment.

## Each release

1. **Bump the version** in `src/ProseForge.Game/ProseForge.Game.csproj`:
   - `ApplicationVersion` is the build number, which must go up on every store upload (Play refuses
     one it has seen).
   - `ApplicationDisplayVersion` is the version players see (1.0, 1.1, …).
2. **Build:**
   ```sh
   make build-android-release   # signed AAB
   make build-ios-release       # IPA
   ```
   Both stage the pictures as WebP first (`make optimize`, below), so the download is about half
   the size. Check that the build log shows the optimize report and `ok` for every pack.
3. **Check the release builds on a device** before uploading: one full loop (collect → send on →
   sell), save and resume, pause on background. Release builds trim and compile ahead of time, so a
   debug run doesn't prove them.
   ```sh
   make deploy-android-release DEVICE=<serial from adb devices>
   ```
4. **Upload:**
   ```sh
   make check-play-auth                  # proves the Play credential, uploads nothing
   make upload-android                   # a DRAFT on the internal track; reaches nobody yet
   make upload-ios                       # App Store Connect; TestFlight processes it
   make asc-status                       # builds and tester groups
   scripts/play/.venv/bin/python scripts/play/upload.py --tracks   # every Play track's state
   ```
   Play's API names differ from its console labels: `internal` = Internal testing, `alpha` =
   Closed testing, `beta` = Open testing, `production` = Production.

## Optimized pictures

The theme and gallery pictures in the repo are full-quality PNG masters, about 90 MB in all, and
the app is built to read them directly. That keeps art changes simple: replace a PNG, run the game.
A store build doesn't need that quality, so release builds ship a WebP copy instead, about a fifth
of the size. On 1.4 the Android package went from 127 MB to 55 MB.

```sh
make optimize        # stage themes/ and gallery/ as WebP in build/packaged, then theme-check that copy
make optimize-check  # fail if build/packaged is out of date with the masters, or over budget
```

- **Release builds run `make optimize` on their own.** `build-android-release`,
  `deploy-android-release` and `build-ios-release` all stage a fresh copy, then pack from it.
- **`OPTIMIZE=0` ships the masters instead,** for example to rule out compression while chasing an
  art bug: `make build-android-release OPTIMIZE=0`. The app reads PNG and WebP, in any build and even
  mixed within one pack, so either way works.
- **The masters are never changed.** The public copy of the repo carries the PNGs, because they're
  what a reader edits.
- **How it decides what to redo:** `build/packaged/manifest.json` records each master's SHA-256.
  A picture is reconverted only when its master's content changed, and the output of a deleted
  master is removed. It compares content, not timestamps, which a checkout rewrites.
- **Budget:** one staged picture over 1,500 KB, or more than 40 MB staged in all, fails the build.
  Change the limits with `python3 tools/optimize/optimize.py check --max-picture-kb N --max-total-mb N`.
- **Needs `cwebp`:** `brew install webp` on macOS, `apt install webp` on Linux.
- **Tests:** `python3 -m unittest discover tools/optimize`, plus the loader's PNG and WebP size tests
  in `make test`.

### Troubleshooting

- **My new art isn't in the release build.** Run `make optimize-check`. If it names your file, the
  staged copy is behind: run `make optimize`, then rebuild. If it passes, check the art is in the
  master: the build packs what `themes/` holds, nothing else. Then check the right build is on the
  device: Play shows the version code, and `adb shell dumpsys package <app id>` shows `lastUpdateTime`.
- **`optimize-check` says a file changed, is new, or was deleted.** The masters moved since the last
  optimize. `make optimize` fixes it; the release targets do this themselves.
- **"isn't from any master".** Something was put into `build/packaged` by hand. It isn't trusted:
  `make optimize` removes it. Change the master, never the staged copy.
- **"still names art/….png, not its WebP".** A staged `theme.json` wasn't rewritten, usually because
  it was copied by hand. Run `make optimize`.
- **Over budget.** The message names the picture. Check its master isn't far bigger than it needs
  to be (a background is 1080×1920 and a cut-out about 1254×1254). Resize the master rather than
  raising the limit.
- **Something looks wrong only in the release build.** Rebuild with `OPTIMIZE=0`. If the problem goes
  away, it's the compression: raise `--quality` in `tools/optimize/optimize.py` (a new quality
  reconverts everything). If it stays, it isn't the pictures.
- **Start completely fresh:** `rm -rf build/packaged && make optimize`. This is always safe, and takes
  about 12 seconds.

## Notes
- **Xcode:** the csproj sets `ValidateXcodeVersion=false` because the .NET iOS SDK can pin an older
  Xcode than the one installed (dotnet/maui#34295). Remove it once the iOS workload catches up.
- **Orbit** (`Bijington.Orbit.Engine` 0.2.2-preview.1) is a prerelease. It compiled under iOS
  device Release (full ahead-of-time) with no warnings on 2026-10-29. Only a signed device run proves
  it at runtime.
- The release targets are written for macOS. On Windows, set `ANDROID_SDK` and `JDK` in `.env.local`;
  the iOS targets need a Mac.
