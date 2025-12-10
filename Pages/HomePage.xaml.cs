using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Navigation;
using CMDevicesManager.Services;
using CMDevicesManager.ViewModels;

namespace CMDevicesManager.Pages
{
    /// <summary>
    /// Interaction logic for HomePage.xaml
    /// </summary>
    public partial class HomePage : Page
    {
        public HomePage()
        {
            InitializeComponent();

            // 不缓存 Page，离开后释放，返回时重新创建 -> 计时器重新启动
            JournalEntry.SetKeepAlive(this, false);

            // 使用单例指标服务
            ISystemMetricsService service = RealSystemMetricsService.Instance;
            DataContext = new HomeViewModel(service);

            // 页面生命周期 -> 启动/停止刷新
            Loaded += HomePage_Loaded;
            Unloaded += HomePage_Unloaded;

            // 防止父级滚动干扰
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key is Key.Up or Key.Down or Key.PageUp or Key.PageDown or Key.Home or Key.End)
                    e.Handled = true;
            };
        }

        private void HomePage_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                // 兼容两种命名
                vm.OnNavigatedTo();
            }
            MainScrollViewer?.Focus();

        }

        private void HomePage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is HomeViewModel vm)
            {
                vm.OnNavigatedFrom();
                vm.Dispose(); // 因为 KeepAlive = false，新页面会重新创建新的 VM
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                // 计算滚动偏移量
                // Delta 为正表示向上滚动，为负表示向下滚动
                // 每次滚动约 120 单位，我们将其转换为像素
                double offset = scrollViewer.VerticalOffset - (e.Delta / 3.0);

                // 应用新的偏移量
                scrollViewer.ScrollToVerticalOffset(offset);

                // 标记事件已处理，防止事件继续冒泡
                e.Handled = true;
            }
        }
    }
}
