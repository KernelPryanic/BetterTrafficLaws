# Better Traffic Laws - build / lint / test / package
#
# Conventions mirror /d/workspace/mass AGENTS.md: `make build`, `make lint`,
# `make test` are the entry points; `make package` produces an upload-ready zip.
# This is a SHVDN3 C# script mod, so build is MSBuild; there is no test runner
# (the game is the integration test).
#
# MSBuild is discovered via vswhere; override if needed:
#   make build MSBUILD="/c/path/to/MSBuild.exe"

PROJECT     := Better Traffic Laws.csproj
CONFIG      ?= Release
PLATFORM    ?= x64

# Mod identity / packaging. DLL name is the AssemblyName.
DLL         := BetterTrafficLaws.dll
DIST        := dist
STAGE       := $(DIST)/scripts
ZIP         := $(DIST)/BetterTrafficLaws.zip

# DLLs the player already has from their ScriptHookV .NET install - referenced
# (SpecificVersion=False, not Private) but must NOT be bundled. This mod has no
# Private dependencies, so only its own DLL ships.
PLAYER_PROVIDED := LemonUI.dll LemonUI.SHVDN3.dll ScriptHookVDotNet3.dll

VSWHERE := /c/Program Files (x86)/Microsoft Visual Studio/Installer/vswhere.exe
MSBUILD ?= $(shell "$(VSWHERE)" -latest -requires Microsoft.Component.MSBuild \
             -find 'MSBuild\**\Bin\MSBuild.exe' 2>/dev/null | head -1)

MSB := "$(MSBUILD)" "$(PROJECT)" -nologo -v:minimal \
       -p:Configuration=$(CONFIG) -p:Platform=$(PLATFORM)

.DEFAULT_GOAL := build
.PHONY: build lint test package clean rebuild help

build: ## compile the mod (Release x64) to bin/
	@test -n "$(MSBUILD)" || { echo "MSBuild not found; pass MSBUILD=/path/to/MSBuild.exe"; exit 1; }
	$(MSB) -t:Build

lint: ## static check (a clean compile is the static check for C#)
	@test -n "$(MSBUILD)" || { echo "MSBuild not found; pass MSBUILD=/path/to/MSBuild.exe"; exit 1; }
	$(MSB) -t:Rebuild -p:WarningLevel=4

test: ## no automated tests - the game is the integration test
	@echo "No test runner: copy bin/$(CONFIG)/BetterTrafficLaws.dll (+ .ini) into"
	@echo "the game's scripts/ folder and reload scripts in-game to test."

rebuild: ## clean then build
	@test -n "$(MSBUILD)" || { echo "MSBuild not found; pass MSBUILD=/path/to/MSBuild.exe"; exit 1; }
	$(MSB) -t:Rebuild

package: build ## build, then zip a gta5-mods.com-ready archive into dist/
	@rm -rf "$(STAGE)" "$(ZIP)"
	@mkdir -p "$(STAGE)"
	@# Ship the mod DLL plus any dependency the build emitted (Private refs),
	@# excluding the assemblies that come from the player's SHVDN/LemonUI install.
	@for dll in bin/$(CONFIG)/*.dll; do \
		name=$$(basename "$$dll"); \
		case " $(PLAYER_PROVIDED) " in *" $$name "*) continue;; esac; \
		cp "$$dll" "$(STAGE)/"; \
	done
	@test -f "$(STAGE)/$(DLL)" || { echo "build output missing $(DLL)"; exit 1; }
	@powershell -NoProfile -Command "Compress-Archive -Path '$(DIST)/scripts' -DestinationPath '$(ZIP)' -Force"
	@rm -rf "$(STAGE)"
	@echo "packaged $(ZIP):"
	@powershell -NoProfile -Command "Add-Type -A System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::OpenRead((Resolve-Path '$(ZIP)')).Entries | ForEach-Object { '  ' + \$$_.FullName }"

clean: ## remove build output (bin/, obj/, dist/)
	@rm -rf bin obj $(DIST)
	@echo "cleaned bin/, obj/ and $(DIST)/"

help: ## list targets
	@grep -E '^[a-zA-Z_-]+:.*?## .*$$' $(MAKEFILE_LIST) | \
		awk 'BEGIN {FS = ":.*?## "}; {printf "  \033[36m%-10s\033[0m %s\n", $$1, $$2}'
