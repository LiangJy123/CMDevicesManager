using System.Windows;

namespace CMDevicesManager.Windows
{
    public partial class PlayModePromptDialog : Window
    {
        public bool GoToPlayMode { get; private set; }
        public bool SuppressFuture { get; private set; }

        public PlayModePromptDialog()
        {
            InitializeComponent();
        }

        private void GoButton_Click(object sender, RoutedEventArgs e)
        {
            GoToPlayMode = true;
            SuppressFuture = DontRemindCheck.IsChecked == true;
            DialogResult = true;
        }

        private void LaterButton_Click(object? sender, RoutedEventArgs e)
        {
            GoToPlayMode = false;
            SuppressFuture = DontRemindCheck.IsChecked == true;
            DialogResult = false;
        }

        // 允许拖动无边框窗口
        private void WindowDragArea_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
                DragMove();
        }
    }
}