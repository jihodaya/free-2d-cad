# Free 2D CAD 배포본 빌드: .NET이 설치되지 않은 PC에서도 실행되는 단일 실행 파일(win-x64)을 만든다.
# 사용법: powershell -ExecutionPolicy Bypass -File build\publish.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root 'dist\Free2DCad'

# 사용자가 실행 파일 옆 폴더(Templates, Blocks, Fonts, Patterns, Linetypes)에 넣어 둔 파일은 지우지 않고 보관했다가 되돌린다
$userDirs = @('Templates', 'Blocks', 'Fonts', 'Patterns', 'Linetypes')
$keep = Join-Path ([System.IO.Path]::GetTempPath()) ('free2dcad-userdirs-' + [guid]::NewGuid().ToString('N'))
if (Test-Path $out) {
	New-Item -ItemType Directory -Force $keep | Out-Null
	foreach ($d in $userDirs) {
		$src = Join-Path $out $d
		if (Test-Path $src) { Copy-Item $src (Join-Path $keep $d) -Recurse -Force }
	}
	Remove-Item $out -Recurse -Force
}

dotnet publish (Join-Path $root 'src\Free2DCad.App\Free2DCad.App.csproj') `
	-c Release -r win-x64 --self-contained true `
	-p:PublishSingleFile=true `
	-p:IncludeNativeLibrariesForSelfExtract=true `
	-p:DebugType=None `
	-o $out
if ($LASTEXITCODE -ne 0) { throw "publish 실패" }

# 네이티브 라이브러리의 디버그 기호는 배포에 필요 없음
Get-ChildItem $out -Filter *.pdb | Remove-Item
# 스크립트에 한글 경로를 쓰면 PowerShell 5.1에서 깨지므로 와일드카드로 복사
Copy-Item (Join-Path $root 'docs\*.md') $out
# 라이선스 (프로그램과 함께 배포)
Copy-Item (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.md') $out
# 해치 패턴 폴더 (실행 파일 옆 Patterns\*.pat: 사용자 패턴 → 해치 패턴 목록)
Copy-Item (Join-Path $root 'build\Patterns') (Join-Path $out 'Patterns') -Recurse
# 사용자 글꼴 폴더 (실행 파일 옆 Fonts: 샘플 무료 글꼴 + fontmap.txt + 라이선스)
Copy-Item (Join-Path $root 'build\Fonts') (Join-Path $out 'Fonts') -Recurse
# 보관한 사용자 파일 되돌리기 (배포본에 같은 이름의 파일이 있으면 배포본 것을 쓴다; README.txt는 프로그램이 다시 만든다)
if (Test-Path $keep) {
	foreach ($d in $userDirs) {
		$saved = Join-Path $keep $d
		if (-not (Test-Path $saved)) { continue }
		Get-ChildItem $saved -Recurse -File | ForEach-Object {
			$rel = $_.FullName.Substring($saved.Length).TrimStart([char]92)
			$dest = Join-Path (Join-Path $out $d) $rel
			if (-not (Test-Path $dest)) {
				New-Item -ItemType Directory -Force (Split-Path -Parent $dest) | Out-Null
				Copy-Item $_.FullName $dest
			}
		}
	}
	Remove-Item $keep -Recurse -Force
}
Get-ChildItem $out | Select-Object Name, @{ n = 'MB'; e = { [math]::Round($_.Length / 1MB, 1) } }
Write-Host "배포본: $out"
