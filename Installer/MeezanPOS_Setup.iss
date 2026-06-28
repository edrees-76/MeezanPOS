; ===================================================
; MeezanPOS Inno Setup Script
; منظومة ميزان للمطاعم - MeezanPOS v1.0
; Designer: م. إدريس فتح الله الهرى
; ===================================================

[Setup]
AppName=منظومة ميزان للمطاعم
AppVersion=1.0.0
AppVerName=منظومة ميزان للمطاعم v1.0
AppPublisher=م. إدريس فتح الله الهرى
AppPublisherURL=mailto:EdreesElhery@gmail.com
AppSupportPhone=0925126355
AppCopyright=جميع الحقوق محفوظة © 2026 — EDREES .F. ELHERY
DefaultDirName={autopf}\MeezanPOS
DefaultGroupName=منظومة ميزان للمطاعم
AllowNoIcons=yes
LicenseFile=d:\Meezan sys\Installer\license_ar.txt
OutputDir=d:\Meezan sys\Installer\Output
OutputBaseFilename=MeezanPOS_Setup_v1.0
SetupIconFile=d:\Meezan sys\Installer\meezan_icon.ico
WizardImageFile=d:\Meezan sys\Installer\meezan_logo.bmp
WizardSmallImageFile=d:\Meezan sys\Installer\designer_logo.bmp
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardResizable=no
DisableWelcomePage=no
ShowLanguageDialog=no
LanguageDetectionMethod=none
MinVersion=6.2
PrivilegesRequired=admin

[Languages]
Name: "arabic"; MessagesFile: "compiler:Languages\Arabic.isl"

[Messages]
arabic.BeveledLabel=منظومة ميزان للمطاعم © 2026

[CustomMessages]
arabic.WelcomeLabel1=مرحباً بك في معالج تثبيت%nمنظومة ميزان للمطاعم
arabic.WelcomeLabel2=ستقوم هذه الأداة بتثبيت%nمنظومة ميزان للمطاعم - MeezanPOS v1.0%nعلى جهازك بشكل احترافي وآمن.%n%nانقر على التالي للمتابعة.
arabic.FinishedHeadingLabel=اكتمل تثبيت منظومة ميزان للمطاعم
arabic.FinishedLabel=تم تثبيت المنظومة بنجاح على جهازك.%nيمكنك الآن البدء في استخدامها.
arabic.ClickFinish=انقر على إنهاء لإغلاق معالج التثبيت.

[Tasks]
Name: "desktopicon"; \
  Description: "إنشاء اختصار على سطح المكتب"; \
  GroupDescription: "اختصارات إضافية:"; \
  Flags: checkedonce

Name: "taskbaricon"; \
  Description: "إضافة إلى شريط المهام"; \
  GroupDescription: "اختصارات إضافية:"; \
  Flags: checkedonce

[Files]
Source: "d:\Meezan sys\Publish\*"; \
  DestDir: "{app}"; \
  Flags: ignoreversion recursesubdirs createallsubdirs

Source: "d:\Meezan sys\MeezanPOS\Presentation\Resources\designer_logo.png"; \
  DestDir: "{app}\Resources"; \
  Flags: ignoreversion

[Icons]
Name: "{group}\منظومة ميزان للمطاعم"; \
  Filename: "{app}\MeezanPOS.exe"

Name: "{group}\إلغاء تثبيت المنظومة"; \
  Filename: "{uninstallexe}"

Name: "{autodesktop}\منظومة ميزان للمطاعم"; \
  Filename: "{app}\MeezanPOS.exe"; \
  Tasks: desktopicon

Name: "{userprograms}\منظومة ميزان للمطاعم"; \
  Filename: "{app}\MeezanPOS.exe"; \
  Tasks: taskbaricon

[Run]
Filename: "{app}\MeezanPOS.exe"; \
  Description: "تشغيل منظومة ميزان للمطاعم الآن"; \
  Flags: nowait postinstall skipifsilent

[Code]

var
  AboutPage: TWizardPage;
  DesignerPage: TWizardPage;

procedure InitializeWizard;
var
  AboutMemo: TMemo;
  DesignerMemo: TMemo;
begin

  // --- PAGE 2: About the System ---
  AboutPage := CreateCustomPage(
    wpLicense,
    'عن منظومة ميزان للمطاعم',
    'المميزات والإمكانيات'
  );

  AboutMemo := TMemo.Create(AboutPage);
  AboutMemo.Parent := AboutPage.Surface;
  AboutMemo.Left := 0;
  AboutMemo.Top := 0;
  AboutMemo.Width := AboutPage.SurfaceWidth;
  AboutMemo.Height := AboutPage.SurfaceHeight;
  AboutMemo.ScrollBars := ssVertical;
  AboutMemo.ReadOnly := True;
  AboutMemo.Font.Size := 10;
  AboutMemo.Lines.Text :=
    'منظومة ميزان للمطاعم — MeezanPOS v1.0' + #13#10 +
    '================================================' + #13#10 +
    '' + #13#10 +
    'منظومة متكاملة لإدارة المطاعم مصممة خصيصاً' + #13#10 +
    'للبيئة العربية وتعمل بالكامل بدون إنترنت.' + #13#10 +
    '' + #13#10 +
    'المميزات والإمكانيات:' + #13#10 +
    '─────────────────────' + #13#10 +
    '✓  إدارة الورديات والمبيعات اليومية' + #13#10 +
    '✓  متابعة المصاريف العامة واليومية' + #13#10 +
    '✓  إدارة أجور العمال وسلفهم' + #13#10 +
    '✓  إدارة حسابات الموردين والفواتير' + #13#10 +
    '✓  الخدمات المصرفية وتتبع الأرصدة' + #13#10 +
    '✓  الحساب الختامي والتقارير المالية' + #13#10 +
    '✓  لوحة قيادة تفاعلية مع رسوم بيانية' + #13#10 +
    '✓  تصدير التقارير بصيغة PDF احترافية' + #13#10 +
    '✓  نسخ احتياطي تلقائي مجدول' + #13#10 +
    '✓  نظام صلاحيات وحماية بكلمة مرور' + #13#10 +
    '✓  واجهة عربية RTL بالكامل' + #13#10 +
    '✓  يعمل بدون إنترنت — Offline' + #13#10 +
    '' + #13#10 +
    'متطلبات التشغيل:' + #13#10 +
    '─────────────────' + #13#10 +
    '•  نظام التشغيل: Windows 8 / 10 / 11' + #13#10 +
    '•  المعالج: 1 GHz أو أسرع' + #13#10 +
    '•  الذاكرة: 2 GB RAM أو أكثر' + #13#10 +
    '•  مساحة القرص: 200 MB';

  // --- PAGE 3: About the Designer ---
  DesignerPage := CreateCustomPage(
    AboutPage.ID,
    'عن المصمم',
    'معلومات المطور ومصمم المنظومة'
  );

  DesignerMemo := TMemo.Create(DesignerPage);
  DesignerMemo.Parent := DesignerPage.Surface;
  DesignerMemo.Left := 0;
  DesignerMemo.Top := 0;
  DesignerMemo.Width := DesignerPage.SurfaceWidth;
  DesignerMemo.Height := DesignerPage.SurfaceHeight;
  DesignerMemo.ScrollBars := ssVertical;
  DesignerMemo.ReadOnly := True;
  DesignerMemo.Font.Size := 10;
  DesignerMemo.Lines.Text :=
    'م. إدريس فتح الله الهرى' + #13#10 +
    'EDREES .F. ELHERY' + #13#10 +
    'AI-Powered Software Developer' + #13#10 +
    '================================================' + #13#10 +
    '' + #13#10 +
    'مطور برامج متخصص في بناء منظومات إدارية' + #13#10 +
    'متكاملة باستخدام تقنيات الذكاء الاصطناعي.' + #13#10 +
    'يجمع بين الخبرة التقنية العميقة والفهم الدقيق' + #13#10 +
    'لاحتياجات بيئة العمل العربية.' + #13#10 +
    '' + #13#10 +
    'معلومات التواصل:' + #13#10 +
    '─────────────────' + #13#10 +
    '📱  0925126355' + #13#10 +
    '📱  0917730110' + #13#10 +
    '📍  بنغازي — ليبيا' + #13#10 +
    '✉   EdreesElhery@gmail.com' + #13#10 +
    '' + #13#10 +
    '================================================' + #13#10 +
    'جميع الحقوق محفوظة © 2026';

  // --- Welcome Page Text ---
  WizardForm.WelcomeLabel1.Caption :=
    'مرحباً بك في معالج تثبيت' + #13#10 +
    'منظومة ميزان للمطاعم';

  WizardForm.WelcomeLabel2.Caption :=
    'ستقوم هذه الأداة بتثبيت' + #13#10 +
    'منظومة ميزان للمطاعم - MeezanPOS v1.0' + #13#10 +
    'على جهازك بشكل احترافي وآمن.' + #13#10 +
    '' + #13#10 +
    'انقر على التالي للمتابعة.';

end;

// =============================================
// UNINSTALL — Show data location, never delete
// =============================================

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataPath: String;
  BackupPath: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    DataPath   := ExpandConstant('{localappdata}\MeezanPOS');
    BackupPath := ExpandConstant('{localappdata}\MeezanPOS\Backups');

    MsgBox(
      'تم إلغاء تثبيت منظومة ميزان للمطاعم بنجاح.' + #13#10 +
      '' + #13#10 +
      'بياناتك محفوظة ولم يتم حذفها.' + #13#10 +
      '' + #13#10 +
      'قاعدة البيانات:' + #13#10 +
      DataPath + '\Meezan.db' + #13#10 +
      '' + #13#10 +
      'النسخ الاحتياطية:' + #13#10 +
      BackupPath,
      mbInformation,
      MB_OK
    );
  end;
end;
