namespace CDMDevicesManagerDevWinUI
{
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public static Window MainWindow = Window.Current;
        public static IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
        public JsonNavigationService NavService { get; set; }
        public IThemeService ThemeService { get; set; }

        public App()
        {
            this.InitializeComponent();
            NavService = new JsonNavigationService();

            // Enables Multicore JIT with the specified profile
            System.Runtime.ProfileOptimization.SetProfileRoot(Constants.RootDirectoryPath);
            System.Runtime.ProfileOptimization.StartProfile("Startup.Profile");
        }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            MainWindow = new MainWindow();

            MainWindow.Title = MainWindow.AppWindow.Title = ProcessInfoHelper.ProductNameAndVersion;
            MainWindow.AppWindow.SetIcon("Assets/AppIcon.ico");

            ThemeService = new ThemeService(MainWindow);

            MainWindow.Activate();

            InitializeApp();
        }

        private async void InitializeApp()
        {
            if (RuntimeHelper.IsPackaged())
            {
                ContextMenuItem menu = new ContextMenuItem
                {
                    Title = "Open CDMDevicesManagerDevWinUI Here",
                    Param = @"""{path}""",
                    AcceptFileFlag = (int)FileMatchFlagEnum.All,
                    AcceptDirectoryFlag = (int)(DirectoryMatchFlagEnum.Directory | DirectoryMatchFlagEnum.Background | DirectoryMatchFlagEnum.Desktop),
                    AcceptMultipleFilesFlag = (int)FilesMatchFlagEnum.Each,
                    Index = 0,
                    Enabled = true,
                    Icon = ProcessInfoHelper.GetFileVersionInfo().FileName,
                    Exe = "CDMDevicesManagerDevWinUI.exe"
                };

                ContextMenuService menuService = new ContextMenuService();
                await menuService.SaveAsync(menu);
            }
        }
    }

}
