#!/usr/bin/env bash
# 在 Linux 上交叉編譯 Windows x64 版本。需要 .NET 8 SDK（~/.dotnet）。
set -euo pipefail
export PATH="$HOME/.dotnet:$PATH" DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
ROOT="$(cd "$(dirname "$0")" && pwd)"
cd "$ROOT/src"
rm -rf "$ROOT/out"

# 1) 框架相依、單一檔案（需安裝 .NET 8 Desktop Runtime x64）
dotnet publish -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o "$ROOT/out/fd"

# 2) 自包含、單一檔案、壓縮（不需安裝任何執行階段；已移除 WPF 等用不到的元件）
dotnet publish -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -o "$ROOT/out/sc"

mkdir -p "$ROOT/dist"
cp "$ROOT/out/fd/PopupTranslator.exe" "$ROOT/dist/PopupTranslator.exe"
cp "$ROOT/out/sc/PopupTranslator.exe" "$ROOT/dist/PopupTranslator-SelfContained.exe"
rm -f "$ROOT/dist/PopupTranslator.zip"
python3 - "$ROOT/dist" <<'PY'
import sys, zipfile, os
d = sys.argv[1]
with zipfile.ZipFile(os.path.join(d, "PopupTranslator.zip"), "w", zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for n in ["PopupTranslator.exe", "PopupTranslator-SelfContained.exe", "README-zh.txt"]:
        z.write(os.path.join(d, n), n)
PY
ls -la "$ROOT/dist"
