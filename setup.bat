@echo off
echo ===========================================
echo Starting MeezanPOS Project Setup...
echo ===========================================

echo 1. Creating WPF Project (.NET 8)...
dotnet new wpf -n MeezanPOS -f net8.0
cd MeezanPOS

echo 2. Installing NuGet Packages...
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.EntityFrameworkCore.Tools
dotnet add package MaterialDesignThemes
dotnet add package CommunityToolkit.Mvvm
dotnet add package LiveChartsCore.SkiaSharpView.WPF

echo 3. Creating Architecture Folders...
mkdir Domain\Entities
mkdir Domain\Enums
mkdir Application\ViewModels
mkdir Application\Services
mkdir Application\Interfaces
mkdir Infrastructure\Data
mkdir Infrastructure\Repositories
mkdir Presentation\Views
mkdir Presentation\Converters

echo ===========================================
echo Setup Completed Successfully!
echo ===========================================
pause
