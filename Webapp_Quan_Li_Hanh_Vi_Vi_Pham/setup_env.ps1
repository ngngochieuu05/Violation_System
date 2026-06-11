$ErrorActionPreference = "Stop"

$ProjectRoot = $PSScriptRoot
$VenvPath = Join-Path $ProjectRoot ".venv"
$RequirementsPath = Join-Path $ProjectRoot "ML\requirements.txt"

Write-Host "Checking for compatible Python 3.11..."
$pythonPath = ""

try {
    # Test if py launcher can find 3.11
    $pythonPath = (py -3.11 -c "import sys; print(sys.executable)").Trim()
} catch {
    Write-Host "Python 3.11 is not found. Installing via winget..."
    try {
        winget install --id Python.Python.3.11 --exact --source winget --accept-package-agreements --accept-source-agreements
        # After installation, try again
        $pythonPath = (py -3.11 -c "import sys; print(sys.executable)").Trim()
    } catch {
        # Fallback to default install path if py launcher is not updated yet
        $fallback = "$env:LOCALAPPDATA\Programs\Python\Python311\python.exe"
        if (Test-Path $fallback) {
            $pythonPath = $fallback
        } else {
            Write-Host "Failed to install or locate Python 3.11. Please install it manually."
            exit 1
        }
    }
}

Write-Host "Using Python executable: $pythonPath"

if (!(Test-Path $VenvPath)) {
    Write-Host "Creating virtual environment at $VenvPath..."
    & $pythonPath -m venv $VenvPath
} else {
    Write-Host "Virtual environment already exists."
}

$venvPython = Join-Path $VenvPath "Scripts\python.exe"

if (!(Test-Path $venvPython)) {
    Write-Host "Error: Virtual environment python.exe not found at $venvPython"
    exit 1
}

Write-Host "Upgrading pip..."
& $venvPython -m pip install --upgrade pip

if (Test-Path $RequirementsPath) {
    Write-Host "Installing requirements from $RequirementsPath..."
    & $venvPython -m pip install -r $RequirementsPath
} else {
    Write-Host "Warning: requirements.txt not found at $RequirementsPath"
}

Write-Host "Setup complete! The C# application will automatically detect and use this virtual environment."
