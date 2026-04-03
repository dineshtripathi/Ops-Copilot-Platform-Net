$file = "g:\ops-copilot-platform\tests\Modules\Packs\OpsCopilot.Modules.Packs.Tests\PackEvidenceExecutorTests.cs"
$content = Get-Content $file -Raw
$updated = $content.Replace(', null, It.IsAny<CancellationToken>()))', ', It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))')
$count = ($content.Length - $updated.Length) / (', null, It.IsAny<CancellationToken>()))'.Length - ', It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))'.Length)
Set-Content $file $updated -NoNewline
Write-Host "Done. Changed approx $( [regex]::Matches($content, [regex]::Escape(', null, It.IsAny<CancellationToken>()')).Count ) occurrences"
