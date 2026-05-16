@echo off
echo ===========================================
echo Building Project and Creating Database...
echo ===========================================

cd MeezanPOS

echo 1. Building the project...
dotnet build
if %errorlevel% neq 0 (
    echo ===========================================
    echo Build failed! Please check the errors above.
    echo ===========================================
    pause
    exit /b %errorlevel%
)

echo.
echo 2. Adding InitialCreate Migration...
dotnet ef migrations add InitialCreate

echo.
echo 3. Updating Database (Creating Meezan.db)...
dotnet ef database update

echo ===========================================
echo Database Created Successfully!
echo ===========================================
pause
