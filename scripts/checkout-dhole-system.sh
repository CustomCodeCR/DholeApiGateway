#!/usr/bin/env bash
# Retrieve the matching DholeSystem deployment definition without reusing stale files.
set -euo pipefail

if [[ $# -ne 1 || ( "$1" != "develop" && "$1" != "master" ) ]]; then
  echo "::error::Expected deployment branch: develop or master"
  exit 2
fi

branch="$1"
repository="https://github.com/CustomCodeCR/DholeSystem.git"
destination="$PWD/.dhole-system"
staging="$(mktemp -d "$PWD/.dhole-system-fetch.XXXXXX")"
trap 'rm -rf "$staging"' EXIT

for attempt in 1 2 3 4; do
  rm -rf "$staging/repository"
  echo "Fetching DholeSystem branch $branch (attempt $attempt/4)"
  if timeout 90s git clone --depth 1 --single-branch --branch "$branch" \
      "$repository" "$staging/repository"; then
    test -r "$staging/repository/deploy/docker-compose.services.yml"
    test -r "$staging/repository/deploy/prepare-environments.sh"
    if [[ "$branch" == "develop" ]]; then
      test -r "$staging/repository/deploy/docker-compose.staging.yml"
    fi
    # Never reuse a partial clone or a stale deployment definition after failure.
    rm -rf "$destination"
    mv "$staging/repository" "$destination"
    echo "DholeSystem $branch fetched and validated successfully"
    exit 0
  fi
  echo "::warning::DholeSystem clone failed on attempt $attempt/4"
  if (( attempt < 4 )); then
    sleep "$(( 3 * attempt ))"
  fi
done

if ! getent ahostsv4 github.com >/dev/null 2>&1; then
  echo "::error::Runner DNS cannot resolve github.com. Check host resolver, DNS and outbound connectivity."
else
  echo "::error::GitHub is resolvable but DholeSystem clone failed. Check HTTPS egress and repository permissions."
fi
exit 1
