# Download BGE-M3 ONNX model for TechieRag.Embedded
# Run this script ONCE to download the model that will be embedded in the DLL
# WARNING: Total download size is ~2.3GB

$ErrorActionPreference = "Stop"

$modelsDir = Join-Path $PSScriptRoot "Models"
$bgeM3Dir = Join-Path $modelsDir "bge-m3"

Write-Host "=== TechieRag.Embedded - BGE-M3 Model Downloader ===" -ForegroundColor Cyan
Write-Host "Total download size: ~2.3GB" -ForegroundColor Yellow
Write-Host ""

# Create directories
if (-not (Test-Path $bgeM3Dir)) {
    New-Item -ItemType Directory -Path $bgeM3Dir -Force | Out-Null
    Write-Host "Created directory: $bgeM3Dir" -ForegroundColor Green
}

# Download BGE-M3 model files from Hugging Face
$baseUrl = "https://huggingface.co/BAAI/bge-m3/resolve/main/onnx"

$files = @(
    @{ Name = "model.onnx"; Url = "$baseUrl/model.onnx"; Size = "725KB" },
    @{ Name = "model.onnx_data"; Url = "$baseUrl/model.onnx_data"; Size = "2.27GB" },
    @{ Name = "tokenizer.json"; Url = "$baseUrl/tokenizer.json"; Size = "17MB" },
    @{ Name = "sentencepiece.bpe.model"; Url = "$baseUrl/sentencepiece.bpe.model"; Size = "5MB" },
    @{ Name = "config.json"; Url = "$baseUrl/config.json"; Size = "698B" }
)

foreach ($file in $files) {
    $destPath = Join-Path $bgeM3Dir $file.Name

    if (Test-Path $destPath) {
        $existingSize = (Get-Item $destPath).Length
        # Skip only if file is reasonably sized (for model.onnx_data, check if > 1GB)
        if ($file.Name -eq "model.onnx_data" -and $existingSize -lt 1GB) {
            Write-Host "Incomplete download, re-downloading: $($file.Name)" -ForegroundColor Yellow
            Remove-Item $destPath -Force
        } else {
            Write-Host "Already exists: $($file.Name) ($existingSize bytes)" -ForegroundColor Yellow
            continue
        }
    }

    Write-Host "Downloading $($file.Name) ($($file.Size))..." -ForegroundColor Cyan

    try {
        $ProgressPreference = 'SilentlyContinue'
        Invoke-WebRequest -Uri $file.Url -OutFile $destPath -UseBasicParsing
        $downloadedSize = (Get-Item $destPath).Length
        Write-Host "Downloaded: $($file.Name) ($downloadedSize bytes)" -ForegroundColor Green
    }
    catch {
        Write-Host "Failed to download $($file.Name): $_" -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "=== Download Complete ===" -ForegroundColor Green
Write-Host ""
Write-Host "BGE-M3 model is now in: $bgeM3Dir" -ForegroundColor Cyan
Write-Host "This will be embedded in the DLL when you build the project." -ForegroundColor Cyan
Write-Host ""
Write-Host "NOTE: The final NuGet package will be ~2.3GB!" -ForegroundColor Yellow
Write-Host ""
Write-Host "To build: dotnet build src/TechieRag.Embedded/TechieRag.Embedded.csproj" -ForegroundColor White
Write-Host "To pack:  dotnet pack src/TechieRag.Embedded/TechieRag.Embedded.csproj -c Release" -ForegroundColor White
