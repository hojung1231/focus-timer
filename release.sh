#!/usr/bin/env bash
# 새 버전 배포 파일 만들기
# 사용법: ./release.sh "바뀐 점 1" "바뀐 점 2" ...
set -euo pipefail
cd "$(dirname "$0")"

dotnet build -c Release
VER=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' FocusTimer.csproj)

mkdir -p download
cp bin/Release/net48/FocusTimer.exe download/
(cd download && rm -f FocusTimer.zip && python3 -c "import zipfile; z=zipfile.ZipFile('FocusTimer.zip','w',zipfile.ZIP_DEFLATED); z.write('FocusTimer.exe'); z.close()")

SHA=$(sha256sum download/FocusTimer.exe | cut -d' ' -f1)
{
  echo "$VER"
  echo "$SHA"
  for note in "$@"; do echo "- $note"; done
} > download/version.txt

echo "v$VER 준비 완료:"
cat download/version.txt
