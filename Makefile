# One More Story. `make build` and `make test` are the checks every change must pass.
#
# `make build` compiles the game's platform-free code (rules, engine, catalogue) through the test
# project, so it needs only the .NET SDK. The full app build needs the .NET MAUI workload and the
# Android or Apple tools, and runs on your machine (see docs/RELEASE.md).

TESTS := tests/ProseForge.Game.Rules.Tests

.PHONY: build test run run-mac coverage theme-check theme-guide art-check optimize optimize-check release-assets

build: ## Compile the game's rules and engine (everything the tests cover).
	dotnet build $(TESTS) -c Release

test: ## Run the rules tests, the release optimizer's tests, and check every theme pack. Any failure exits non-zero.
	dotnet run --project $(TESTS) -c Release
	python3 -m unittest discover tools/optimize
	$(MAKE) theme-check

# Where `make run` can run the game: one line per platform, naming the .NET target it builds.
# Mac and Android are run on our machines. Windows is not tested: none of us has a Windows PC, and
# the recipe may need a different shell there. If you get it working, please send us a patch.
RUN_TFM_mac     := net10.0-maccatalyst
RUN_TFM_android := net10.0-android
RUN_TFM_windows := net10.0-windows10.0.19041.0
RUN_PLATFORMS   := mac android windows

run: ## Build and run the game: make run PLATFORM=mac|android|windows [DEVICE=<adb serial>] (Windows: not tested)
	@test -n "$(RUN_TFM_$(PLATFORM))" || { echo "PLATFORM must be one of: $(RUN_PLATFORMS) (e.g. make run PLATFORM=mac)"; exit 2; }
	dotnet build $(PROJECT) -f $(RUN_TFM_$(PLATFORM)) -c Debug "-t:Build;Run" \
		-p:ApplicationId="$(APP_ID)" -p:ApplicationTitle="$(APP_TITLE)" \
		$(if $(and $(DEVICE),$(filter android,$(PLATFORM))),-p:AdbTarget="-s $(DEVICE)")

run-mac: ## Build and open the game on this Mac (no paid Apple account needed); same as make run PLATFORM=mac
	@$(MAKE) --no-print-directory run PLATFORM=mac

notices: ## Regenerate THIRD_PARTY_NOTICES.md from the packages the app ships (after a restore)
	python3 tools/notices/notices.py

coverage: ## Line coverage of the rules tests, per file (needs: dotnet tool install -g dotnet-coverage)
	rm -rf build/coverage && dotnet build $(TESTS) -c Debug -o build/coverage/bin
	cd build/coverage/bin && dotnet-coverage instrument ProseForge.Game.Rules.Tests.dll >/dev/null \
		&& dotnet-coverage collect -f cobertura -o ../rules.cobertura.xml ./ProseForge.Game.Rules.Tests >/dev/null
	python3 tools/coverage/summary.py build/coverage/rules.cobertura.xml src/ProseForge.Game

optimize: ## Stage release pictures as WebP in build/packaged (masters untouched), then check them
	python3 tools/optimize/optimize.py build --src . --out $(PACKED)
	dotnet run --project tools/ThemeCheck -c Release -- $(PACKED)

optimize-check: ## Fail if build/packaged is out of date with the masters, or over its size budget
	python3 tools/optimize/optimize.py check --src . --out $(PACKED)

# Every release build: a fresh staged copy when OPTIMIZE=1, or a note that the masters ship.
# (Decided when the recipe runs, not when this file is read: OPTIMIZE is set further down.)
release-assets:
	@$(if $(filter 1,$(OPTIMIZE)),$(MAKE) --no-print-directory optimize,echo "OPTIMIZE=0: this release ships the PNG masters, not the optimized copy")

theme-check: ## Check theme packs: make theme-check, or make theme-check THEME=guild
	dotnet run --project tools/ThemeCheck -c Release -- . $(THEME)

theme-guide: ## Draw the art guide artists generate theme backgrounds against (themes/_shared/art-guide-3.png)
	dotnet run --project tools/ThemeCheck -c Release -- --guide 3 themes/_shared/art-guide-3.svg
	rsvg-convert themes/_shared/art-guide-3.svg -o themes/_shared/art-guide-3.png

art-check: ## Lay the art guide over a background: make art-check ART=path/to/background.png (writes build/art-check.png)
	@test -n "$(ART)" || { echo "usage: make art-check ART=path/to/background.png"; exit 2; }
	@mkdir -p build
	dotnet run --project tools/ThemeCheck -c Release -- --guide 3 build/art-guide-overlay.svg overlay
	rsvg-convert build/art-guide-overlay.svg -o build/art-guide-overlay.png
	magick "$(ART)" -resize 1080x1920^ -gravity center -extent 1080x1920 build/art-guide-overlay.png -composite build/art-check.png
	@echo "wrote build/art-check.png"

# --- Release builds and store uploads -------------------------------------------------------
# Your settings live in two files at the top of the folder. Git ignores both, so neither is ever
# committed:
#   .env.local  what makes the build yours: app ID, name, tool paths. Copy env.local.example.
#   .env        signing keys and store credentials. docs/RELEASE.md lists the names.
# Nothing here publishes to the public stores: uploads go to Play's internal testing track (as a
# draft) and to TestFlight.

-include .env
-include .env.local

PROJECT     := src/ProseForge.Game/ProseForge.Game.csproj
APP_ID      ?= com.yourorg.yourgame
# The default is a placeholder: release and store commands refuse it until .env.local sets your own.
PLACEHOLDER_APP_ID := com.yourorg.yourgame
APP_TITLE   ?= One More Story
ANDROID_SDK ?= $(HOME)/Library/Android/sdk
JDK         ?= $(shell /usr/libexec/java_home -v 21 2>/dev/null)
# Release builds ship the theme and gallery pictures as WebP, staged in build/packaged by
# `make optimize` from the PNG masters. OPTIMIZE=0 ships the masters instead. See docs/RELEASE.md.
OPTIMIZE    ?= 1
# Never let OPTIMIZE reach the build's environment: MSBuild reads environment variables as properties,
# case-insensitively, so it would set the C# compiler's own Optimize. OPTIMIZE=1 then fails the build,
# and OPTIMIZE=0 would quietly ship an unoptimized release.
unexport OPTIMIZE
PACKED      := build/packaged
APP_FLAGS   := -p:ApplicationId="$(APP_ID)" -p:ApplicationTitle="$(APP_TITLE)" \
               $(if $(filter 1,$(OPTIMIZE)),-p:AssetRoot="$(CURDIR)/$(PACKED)/")
ANDROID_PUBLISH := src/ProseForge.Game/bin/Release/net10.0-android/publish
IOS_PUBLISH     := src/ProseForge.Game/bin/Release/net10.0-ios/ios-arm64/publish

# No bare `export`: it would put every secret in .env into the environment of every recipe, where
# any other process on the machine can read it (`ps -wwEo command=`). Make expands $(VAR) itself,
# so each program gets only the value it needs, on its own command line.

# Release and store commands stop here until APP_ID is your own, so nothing is built or uploaded
# under the placeholder.
app-id:
	@test "$(APP_ID)" != "$(PLACEHOLDER_APP_ID)" || { echo "APP_ID is still the placeholder $(PLACEHOLDER_APP_ID). Set your own in .env.local (see env.local.example)."; exit 2; }

.PHONY: app-id build-android-release deploy-android-release build-ios-release upload-android upload-ios check-play-auth \
	check-play-scripts asc-status

build-android-release: release-assets app-id ## Signed Release AAB for Play (needs ANDROID_* signing values in .env)
	@test -n "$(ANDROID_KEYSTORE_PATH)" || (echo "Error: ANDROID_KEYSTORE_PATH not set in .env" && exit 1)
	@# a release never ships notices older than its packages.
	dotnet restore $(PROJECT) >/dev/null && python3 tools/notices/notices.py --check
	@# Leading @ keeps the keystore passwords out of make's echo of the recipe.
	@ANDROID_HOME="$(ANDROID_SDK)" JAVA_HOME="$(JDK)" \
		dotnet publish $(PROJECT) -c Release -f net10.0-android $(APP_FLAGS) \
		-p:AndroidPackageFormat=aab \
		-p:AndroidKeyStore=true \
		-p:AndroidSigningKeyStore="$(ANDROID_KEYSTORE_PATH)" \
		-p:AndroidSigningKeyAlias="$(ANDROID_KEY_ALIAS)" \
		-p:AndroidSigningKeyPass="$(ANDROID_KEY_PASSWORD)" \
		-p:AndroidSigningStorePass="$(ANDROID_KEYSTORE_PASSWORD)"
	@echo "AAB: $(ANDROID_PUBLISH)/$(APP_ID)-Signed.aab"

# Signed release APK on a device, to check the Release build before uploading (DEVICE=<adb serial>).
# Verify by lastUpdateTime, not adb's exit code: a failed install keeps the old timestamp.
deploy-android-release: release-assets app-id ## Signed Release APK installed on DEVICE
	@test -n "$(ANDROID_KEYSTORE_PATH)" || (echo "Error: ANDROID_KEYSTORE_PATH not set in .env" && exit 1)
	@test -n "$(DEVICE)" || (echo "Error: DEVICE not set; pass DEVICE=<serial> from 'adb devices'" && exit 1)
	@ANDROID_HOME="$(ANDROID_SDK)" JAVA_HOME="$(JDK)" \
		dotnet publish $(PROJECT) -c Release -f net10.0-android $(APP_FLAGS) \
		-p:AndroidPackageFormat=apk \
		-p:AndroidKeyStore=true \
		-p:AndroidSigningKeyStore="$(ANDROID_KEYSTORE_PATH)" \
		-p:AndroidSigningKeyAlias="$(ANDROID_KEY_ALIAS)" \
		-p:AndroidSigningKeyPass="$(ANDROID_KEY_PASSWORD)" \
		-p:AndroidSigningStorePass="$(ANDROID_KEYSTORE_PASSWORD)"
	adb -s $(DEVICE) install -r $(ANDROID_PUBLISH)/$(APP_ID)-Signed.apk
	@adb -s $(DEVICE) shell dumpsys package $(APP_ID) | grep -E 'versionName|lastUpdateTime'

build-ios-release: release-assets app-id ## Release IPA for TestFlight (needs IOS_CODESIGN_KEY and IOS_PROVISIONING_PROFILE in .env)
	@test -n "$(IOS_PROVISIONING_PROFILE)" || (echo "Error: IOS_PROVISIONING_PROFILE not set in .env" && exit 1)
	@# An incremental build reused a stale merged manifest after Info.plist changed (the
	@# encryption answer, ITSAppUsesNonExemptEncryption, went missing from the IPA). Always rebuild it.
	rm -f src/ProseForge.Game/obj/Release/net10.0-ios/ios-arm64/AppManifest.plist
	@# a release never ships notices older than its packages.
	dotnet restore $(PROJECT) >/dev/null && python3 tools/notices/notices.py --check
	dotnet publish $(PROJECT) -c Release -f net10.0-ios $(APP_FLAGS) \
		-p:RuntimeIdentifier=ios-arm64 \
		-p:ArchiveOnBuild=true \
		-p:CodesignKey="$(subst ",,$(IOS_CODESIGN_KEY))" \
		-p:CodesignProvision="$(subst ",,$(IOS_PROVISIONING_PROFILE))"
	@echo "IPA: $(IOS_PUBLISH)/ProseForge.Game.ipa"

# Play publisher venv, provisioned on demand (PEP 668 blocks pip --user on Homebrew python).
PLAY_VENV := scripts/play/.venv
PLAY_PY   := $(PLAY_VENV)/bin/python
ASC_VENV  := scripts/asc/.venv
ASC_PY    := $(ASC_VENV)/bin/python

$(PLAY_VENV):
	@python3 -m venv $(PLAY_VENV)
	@$(PLAY_VENV)/bin/pip install --quiet google-auth requests

$(ASC_VENV):
	@python3 -m venv $(ASC_VENV)
	@$(ASC_VENV)/bin/pip install --quiet pyjwt cryptography

# ⛔ Defaults to a DRAFT on the internal testing track, which reaches nobody. A public track needs
#    both TRACK and ROLLOUT named, so a mistyped command can't publish.
upload-android: $(PLAY_VENV) app-id ## Upload the AAB to Play internal testing (draft by default)
	@test -n "$(GOOGLE_PLAY_SERVICE_ACCOUNT_JSON)" || (echo "Error: GOOGLE_PLAY_SERVICE_ACCOUNT_JSON not set in .env" && exit 1)
	@APP_ID="$(APP_ID)" GOOGLE_PLAY_SERVICE_ACCOUNT_JSON="$(GOOGLE_PLAY_SERVICE_ACCOUNT_JSON)" $(PLAY_PY) scripts/play/upload.py \
		--aab "$(or $(AAB),$(ANDROID_PUBLISH)/$(APP_ID)-Signed.aab)" \
		--track "$(or $(TRACK),internal)" \
		$(if $(ROLLOUT),--rollout $(ROLLOUT)) \
		$(if $(NOTES),--notes "$(NOTES)") \
		$(if $(DRY_RUN),--dry-run)

check-play-auth: $(PLAY_VENV) app-id ## Prove the Play credential and grant without uploading anything
	@APP_ID="$(APP_ID)" GOOGLE_PLAY_SERVICE_ACCOUNT_JSON="$(GOOGLE_PLAY_SERVICE_ACCOUNT_JSON)" $(PLAY_PY) scripts/play/upload.py --check

check-play-scripts: $(PLAY_VENV) ## Offline tests for the upload script's guards
	@$(PLAY_PY) scripts/play/test_upload.py

asc-status: $(ASC_VENV) app-id ## App Store Connect: builds, groups and their states
	@APP_ID="$(APP_ID)" APPLE_P8="$(APPLE_P8)" APPLE_KEY_ID="$(APPLE_KEY_ID)" APPLE_UUID="$(APPLE_UUID)" $(ASC_PY) scripts/asc/asc.py status

# ⛔ The leading @ is a secret control: without it make echoes AC_PASSWORD (an Apple app-specific
#    password) to the console. It is passed through the environment as @env:AC_PASSWORD, never on
#    altool's command line, where any other process on the machine could read it.
upload-ios: app-id ## Upload the IPA to App Store Connect (TestFlight processes it; internal group only)
	@test -n "$(APPLE_ID)" || (echo "Error: APPLE_ID not set in .env" && exit 1)
	@AC_PASSWORD="$$(grep '^AC_PASSWORD=' .env | cut -d= -f2- | tr -d '"' | tr -d "'")" xcrun altool --upload-app \
		-f "$(or $(IPA),$(IOS_PUBLISH)/ProseForge.Game.ipa)" -t ios -u "$(APPLE_ID)" -p @env:AC_PASSWORD
	@echo "Uploaded. Check TestFlight processing with: make asc-status"
