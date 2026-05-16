@echo off
chcp 65001 > nul
echo ========================================================
echo        أداة الرفع التلقائي لـ GitHub - منظومة ميزان
echo ========================================================
echo.

:: التحقق من وجود Git
git --version > nul 2>&1
IF %ERRORLEVEL% NEQ 0 (
    echo [خطأ] برنامج Git غير مثبت على جهازك! يرجى تحميله من https://git-scm.com/
    pause
    exit /b
)

echo [1] جاري تهيئة المجلد كمسار Git...
git init

echo [2] جاري إضافة جميع الملفات...
git add .

echo [3] جاري حفظ التعديلات...
git commit -m "النسخة الاحتياطية الأولية - منظومة ميزان للمطاعم"

:: تغيير الفرع الرئيسي إلى main
git branch -M main

echo.
echo ========================================================
echo هل قمت بإنشاء مستودع (Repository) جديد فارغ على موقع GitHub؟
echo ========================================================
echo.
set /p repo_url="يرجى لصق رابط المستودع هنا (مثال: https://github.com/user/MeezanPOS.git) ثم اضغط Enter: "

IF "%repo_url%"=="" (
    echo [خطأ] لم تقم بإدخال الرابط. تم إلغاء العملية.
    pause
    exit /b
)

echo.
echo [4] جاري ربط المنظومة بحسابك على GitHub...
git remote remove origin > nul 2>&1
git remote add origin "%repo_url%"

echo [5] جاري رفع الملفات (قد تظهر لك نافذة لتسجيل الدخول إلى حسابك)...
git push -u origin main

IF %ERRORLEVEL% EQU 0 (
    echo.
    echo ========================================================
    echo [نجاح] تم رفع النسخة الاحتياطية بنجاح إلى GitHub!
    echo ========================================================
) ELSE (
    echo.
    echo [خطأ] حدثت مشكلة أثناء الرفع. تأكد من صحة الرابط واتصالك بالإنترنت.
)

pause
