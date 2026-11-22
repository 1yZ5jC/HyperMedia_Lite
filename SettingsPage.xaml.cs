using HyperMedia_Lite.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace HyperMedia_Lite
{
    /// <summary>
    /// 独立设置页：媒体库管理、访问权限、统计与关于。
    /// </summary>
    public sealed partial class SettingsPage : Page
    {
        private readonly MainViewModel _viewModel = App.MainViewModel;

        public SettingsPage()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private void ClearAccess_Click(object sender, RoutedEventArgs e)
        {
            _viewModel.ClearSavedFiles();
        }
    }
}
