# syntax=docker/dockerfile:1
# ---------------------------------------------------------------------------
# condeco-cli – minimal Linux container
#
# The image expects a config file to be mounted at runtime:
#   docker run --rm -v /path/to/condeco-cli.json:/app/condeco-cli.json \
#              ghcr.io/<owner>/condeco-cli --autobook
#
# The binary is a self-contained .NET executable; no .NET runtime is needed
# inside the image, but glibc *is* required (hence debian-slim, not alpine).
# ---------------------------------------------------------------------------
FROM debian:bookworm-slim

# Install:
# ca-certificates – required for HTTPS calls to the Condeco API
# libicu72        – required by .NET for globalization (dates, timezones, etc.)
#                   Bookworm ships ICU 72; update the version suffix if you
#                   ever rebase onto a newer Debian release.
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
         ca-certificates \
         libicu72 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

# The binary is copied in by the GitHub Actions build step (see
# .github/workflows/docker-publish.yml).
# It is downloaded from the GitHub Release assets before `docker build`.
COPY condeco-cli ./condeco-cli
RUN chmod +x ./condeco-cli

# Default command – override with --checkin or any other flag at `docker run`
ENTRYPOINT ["/app/condeco-cli"]
CMD ["--autobook"]
