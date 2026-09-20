# ICARUS — build, test and publish (Linux only, ICARUS-110).

DOTNET ?= dotnet
CONFIG ?= Release
RIDS   ?= linux-x64 linux-arm64

.PHONY: build test check publish clean

build:
	$(DOTNET) build -warnaserror

test:
	$(DOTNET) test --nologo

## Quality gate: warnings as errors + the offline suite.
check: build test

## Self-contained single-file binaries for every target RID.
publish:
	@for rid in $(RIDS); do \
	  echo "publishing $$rid"; \
	  $(DOTNET) publish src/Icarus.Cli -c $(CONFIG) -r $$rid --self-contained true \
	    -p:PublishSingleFile=true -o artifacts/$$rid || exit 1; \
	done

clean:
	rm -rf artifacts
	$(DOTNET) clean
