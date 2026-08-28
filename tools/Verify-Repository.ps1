param(
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$temporaryBuild = Join-Path $env:TEMP ("orla-verify-" + [Guid]::NewGuid().ToString('N'))
$errors = New-Object Collections.Generic.List[string]

try {
    if (-not $SkipBuild) {
        & (Join-Path $root 'Build.ps1') -OutputDirectory $temporaryBuild | Out-Null
        $binary = Join-Path $temporaryBuild 'Orla.exe'
        if (-not (Test-Path -LiteralPath $binary)) {
            $errors.Add('Build did not produce Orla.exe.')
        } else {
            $version = (Get-Item -LiteralPath $binary).VersionInfo.FileVersion
            if ($version -notmatch '^\d+\.\d+\.\d+\.0$') {
                $errors.Add("Unexpected binary version: $version")
            }
        }
    }

    $svgFiles = @(Get-ChildItem -LiteralPath (Join-Path $root 'docs\images') -Filter '*.svg' -File)
    foreach ($svg in $svgFiles) {
        try {
            [xml]$document = Get-Content -LiteralPath $svg.FullName -Raw
            if ($document.DocumentElement.LocalName -ne 'svg') {
                $errors.Add("Invalid SVG root: $($svg.FullName)")
            }
            $title = $document.SelectSingleNode("/*[local-name()='svg']/*[local-name()='title']")
            $description = $document.SelectSingleNode("/*[local-name()='svg']/*[local-name()='desc']")
            if (-not $title -or [string]::IsNullOrWhiteSpace($title.InnerText)) {
                $errors.Add("SVG has no accessible title: $($svg.FullName)")
            }
            if (-not $description -or [string]::IsNullOrWhiteSpace($description.InnerText)) {
                $errors.Add("SVG has no accessible description: $($svg.FullName)")
            }
        } catch {
            $errors.Add("Invalid SVG XML: $($svg.FullName) - $($_.Exception.Message)")
        }
    }

    $markdownFiles = @(Get-ChildItem -LiteralPath $root -Filter '*.md' -File -Recurse | Where-Object {
        $_.FullName -notlike '*\.git\*' -and $_.FullName -notlike '*\.codex-build\*'
    })
    $linkPattern = [regex]'!?\[[^\]]*\]\((?<target>[^)]+)\)'
    foreach ($markdown in $markdownFiles) {
        $content = Get-Content -LiteralPath $markdown.FullName -Raw
        foreach ($match in $linkPattern.Matches($content)) {
            $target = $match.Groups['target'].Value.Trim()
            if ($target.StartsWith('<') -and $target.EndsWith('>')) {
                $target = $target.Substring(1, $target.Length - 2)
            }
            if ($target -match '^(?:https?://|mailto:|#)') { continue }
            $target = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($target)) { continue }
            $decoded = [Uri]::UnescapeDataString($target)
            $resolved = Join-Path $markdown.DirectoryName $decoded
            if (-not (Test-Path -LiteralPath $resolved)) {
                $relative = $markdown.FullName.Substring($root.Length + 1)
                $errors.Add("Broken local link in $relative`: $target")
            }
        }
    }

    $gitDiffChecked = $false
    # Git for Windows can block when powershell.exe is launched through the
    # WSL interop bridge. In that environment, run `git diff --check` from
    # WSL itself; native Windows and GitHub Actions use the check below.
    if (-not $env:WSLENV) {
        $gitCommand = Get-Command git -ErrorAction SilentlyContinue
        $gitExecutable = if ($gitCommand) {
            $gitCommand.Source
        } else {
            Join-Path $env:ProgramFiles 'Git\cmd\git.exe'
        }
        if (-not (Test-Path -LiteralPath $gitExecutable)) {
            $errors.Add('Git was not found in PATH or Program Files.')
        } else {
            $startInfo = New-Object Diagnostics.ProcessStartInfo
            $startInfo.FileName = $gitExecutable
            $startInfo.Arguments = 'diff --check'
            $startInfo.WorkingDirectory = $root
            $startInfo.UseShellExecute = $false
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $process = [Diagnostics.Process]::Start($startInfo)
            $standardOutput = $process.StandardOutput.ReadToEnd()
            $standardError = $process.StandardError.ReadToEnd()
            $process.WaitForExit()
            if ($process.ExitCode -ne 0) {
                $errors.Add("git diff --check failed: $standardOutput $standardError")
            } else {
                $gitDiffChecked = $true
            }
        }
    }

    if ($errors.Count -gt 0) {
        throw ($errors -join [Environment]::NewLine)
    }

    [pscustomobject]@{
        Passed = $true
        SvgFiles = $svgFiles.Count
        MarkdownFiles = $markdownFiles.Count
        BuildChecked = -not $SkipBuild
        GitDiffChecked = $gitDiffChecked
    } | ConvertTo-Json
} finally {
    if (Test-Path -LiteralPath $temporaryBuild) {
        Remove-Item -LiteralPath $temporaryBuild -Recurse -Force
    }
}
