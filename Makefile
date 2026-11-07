# proseforge-game. CI runs `make build` and `make test` on every PR and on
# master after each merge (Jenkinsfile), and the PR can't merge unless both pass.
#
# ⚠️ These used to be placeholders that always passed, so a green check proved nothing about the
#    code. They now build and run the rules tests. `make build` compiles the game's
#    platform-free code (rules, engine, catalogue) through the test project; the full MAUI app
#    build needs the Android/iOS workloads and stays a local step (see docs/RELEASE.md).

TESTS := tests/ProseForge.Game.Rules.Tests

.PHONY: build test coverage theme-check theme-guide art-check

build: ## Compile the game's rules and engine (everything the tests cover).
	dotnet build $(TESTS) -c Release

test: ## Run the rules tests and check every theme pack. Any failure exits non-zero.
	dotnet run --project $(TESTS) -c Release
	$(MAKE) theme-check

coverage: ## Line coverage of the rules tests, per file (needs: dotnet tool install -g dotnet-coverage)
	rm -rf build/coverage && dotnet build $(TESTS) -c Debug -o build/coverage/bin
	cd build/coverage/bin && dotnet-coverage instrument ProseForge.Game.Rules.Tests.dll >/dev/null \
		&& dotnet-coverage collect -f cobertura -o ../rules.cobertura.xml ./ProseForge.Game.Rules.Tests >/dev/null
	python3 tools/coverage/summary.py build/coverage/rules.cobertura.xml src/ProseForge.Game

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
