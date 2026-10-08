@chcp 65001 >nul
@echo off
rem تشغيل منظومة ميزان على قاعدة البيانات التجريبية (لا تمس البيانات الحقيقية)
rem يجب إغلاق المنظومة العادية أولاً لأنها تسمح بنسخة واحدة فقط مفتوحة.
set "MEEZANPOS_DATA_DIR=%LOCALAPPDATA%\MeezanPOS_Demo"
if not exist "%MEEZANPOS_DATA_DIR%\Meezan.db" (
    echo لا توجد بيانات تجريبية بعد. شغّل أولاً: dotnet run --project Tools\MeezanPOS.DemoData
    pause
    exit /b 1
)
start "" "%~dp0..\MeezanPOS\bin\Debug\net8.0-windows\MeezanPOS.exe"
