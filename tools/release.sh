#!/bin/bash
# Publishes dist/Lanppart3-Final-Sentence.exe (built by mod/package.sh) as the GitHub release
# v<version of launcher/Lanppart3Launcher.csproj>. The launchers already installed then offer the update.
# Uses the GitHub credentials stored for git (Git Credential Manager). Usage: tools/release.sh ["notes"]
set -e
trap 'echo "release.sh: failed at line $LINENO" >&2' ERR
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
REPO="xams0/final-sentence-enhanced"
ASSET="${ASSET:-$ROOT/dist/Lanppart3-Final-Sentence.exe}"
VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/launcher/Lanppart3Launcher.csproj")
TAG="v$VERSION"
NOTES="${1:-Lanppart3 $TAG}"
[ -f "$ASSET" ] || { echo "Missing $ASSET: run mod/package.sh first"; exit 1; }

TOKEN=$(printf 'protocol=https\nhost=github.com\n\n' | git credential fill | sed -n 's/^password=//p')
[ -n "$TOKEN" ] || { echo "No GitHub credentials stored for git"; exit 1; }
api() { curl -sS -H "Authorization: Bearer $TOKEN" -H "Accept: application/vnd.github+json" "$@"; }

# JSON string of the notes (backslashes, quotes and line breaks escaped).
JSON_NOTES=$(printf '%s' "$NOTES" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | awk '{ printf "%s\\n", $0 }')
BODY="{\"tag_name\":\"$TAG\",\"target_commitish\":\"main\",\"name\":\"Lanppart3 $TAG\",\"body\":\"$JSON_NOTES\"}"
RELEASE=$(api -X POST "https://api.github.com/repos/$REPO/releases" -d "$BODY")
ID=$(printf '%s' "$RELEASE" | grep -o '"id": *[0-9]*' | head -1 | grep -o '[0-9]*$')
[ -n "$ID" ] || { echo "Release not created: $RELEASE"; exit 1; }
api -X POST -H "Content-Type: application/octet-stream" --data-binary @"$ASSET" \
  "https://uploads.github.com/repos/$REPO/releases/$ID/assets?name=Lanppart3-Final-Sentence.exe" > /dev/null
echo "Published $TAG: https://github.com/$REPO/releases/tag/$TAG"
