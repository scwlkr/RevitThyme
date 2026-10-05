@echo off
setlocal
where cargo >nul 2>nul
if errorlevel 1 (
    echo Cargo is required for the project CLI. See SETUP-TODO.md. 1>&2
    exit /b 127
)
set "RUSTUP_AUTO_INSTALL=0"
cargo run --quiet --offline --locked --manifest-path "%~dp0tools\project-cli\Cargo.toml" -- %*
exit /b %errorlevel%
