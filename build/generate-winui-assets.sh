#!/usr/bin/env bash
#
# Generates the MSIX visual-asset set for SimpleText.WinUI from the brand SVG.
# Output: SimpleText.WinUI/Images/*.png (git-ignored; produced in CI before packaging).
#
# Requires: rsvg-convert (librsvg2-bin) and ImageMagick (convert or magick).
# Run from the repository root:  ./build/generate-winui-assets.sh
set -euo pipefail

SVG="branding/simpletext.svg"
OUT="SimpleText.WinUI/Images"

command -v rsvg-convert >/dev/null 2>&1 || { echo "error: rsvg-convert not found (apt: librsvg2-bin)" >&2; exit 1; }
if command -v magick >/dev/null 2>&1; then IM="magick"
elif command -v convert >/dev/null 2>&1; then IM="convert"
else echo "error: ImageMagick not found (apt: imagemagick)" >&2; exit 1; fi

mkdir -p "$OUT"
MASTER="$(mktemp -u).png"
trap 'rm -f "$MASTER"' EXIT
rsvg-convert -w 1024 -h 1024 "$SVG" -o "$MASTER"

# Scales Windows looks for, per asset.
SCALES="100 125 150 200 400"

# square <ManifestName> <baseEdgePx>
square() {
  local name="$1" base="$2"
  for pct in $SCALES; do
    local px=$(( base * pct / 100 ))
    local suffix=""; [ "$pct" = 100 ] || suffix=".scale-$pct"
    "$IM" "$MASTER" -resize "${px}x${px}" "$OUT/${name}${suffix}.png"
  done
}

# padded <ManifestName> <widthPx> <heightPx>  (icon centred on a transparent canvas)
padded() {
  local name="$1" w="$2" h="$3"
  local min=$(( w < h ? w : h ))
  for pct in $SCALES; do
    local cw=$(( w * pct / 100 )) ch=$(( h * pct / 100 ))
    local icon=$(( min * pct / 100 * 80 / 100 ))
    local suffix=""; [ "$pct" = 100 ] || suffix=".scale-$pct"
    "$IM" "$MASTER" -resize "${icon}x${icon}" -background none -gravity center \
      -extent "${cw}x${ch}" "$OUT/${name}${suffix}.png"
  done
}

square StoreLogo          50
square Square44x44Logo    44
square Square71x71Logo    71
square Square150x150Logo  150
square Square310x310Logo  310
padded Wide310x150Logo    310 150
padded SplashScreen       620 300

# Taskbar / app-list target sizes (plated + unplated).
for ts in 16 24 32 48 256; do
  "$IM" "$MASTER" -resize "${ts}x${ts}" "$OUT/Square44x44Logo.targetsize-${ts}.png"
  "$IM" "$MASTER" -resize "${ts}x${ts}" "$OUT/Square44x44Logo.targetsize-${ts}_altform-unplated.png"
done

echo "Generated $(find "$OUT" -name '*.png' | wc -l) assets in $OUT"
