@echo off
chcp 65001 >nul
setlocal enabledelayedexpansion

echo ================================================
echo   ClassScreenLock Web - One Click Start
echo ================================================
echo.

:: Check Python
where python >nul 2>nul
if %errorlevel% neq 0 (
    echo [Error] Python not found. Please install Python 3.8+
    echo Download: https://www.python.org/downloads/
    pause
    exit /b 1
)

:: Check Node.js
where node >nul 2>nul
if %errorlevel% neq 0 (
    echo [Error] Node.js not found. Please install Node.js 18+
    echo Download: https://nodejs.org/
    pause
    exit /b 1
)

echo [OK] Python: 
python --version
echo [OK] Node.js: 
node --version
echo.

:: Check dependencies
echo Checking dependencies...
if not exist "node_modules\" (
    echo Installing frontend dependencies...
    call npm install
    if %errorlevel% neq 0 (
        echo Frontend dependencies installation failed
        pause
        exit /b 1
    )
    echo Frontend dependencies installed
) else (
    echo Frontend dependencies already installed
)

if not exist "backend\__pycache__\" (
    echo Installing backend dependencies...
    cd backend
    call pip install -r requirements.txt
    if %errorlevel% neq 0 (
        echo Backend dependencies installation failed
        cd ..
        pause
        exit /b 1
    )
    cd ..
    echo Backend dependencies installed
) else (
    echo Backend dependencies already installed
)

echo.
echo ================================================
echo   Starting services...
echo ================================================
echo.

:: Start backend
echo [1/2] Starting Python backend...
start "ClassScreenLock Backend" cmd /k "cd backend && python app.py"

:: Wait for backend
echo Waiting for backend to start...
timeout /t 3 /nobreak >nul

:: Start frontend
echo [2/2] Starting frontend development server...
start "ClassScreenLock Frontend" cmd /k "npm run dev"

echo.
echo ================================================
echo   Services started successfully!
echo ================================================
echo.
echo Frontend: http://localhost:5173
echo Backend: http://localhost:5000
echo.
echo Default credentials:
echo   Username: admin
echo   Password: admin123
echo.
echo WARNING: Please change the default password after first login!
echo.
echo To stop services, close the command prompt windows
echo.
pause
