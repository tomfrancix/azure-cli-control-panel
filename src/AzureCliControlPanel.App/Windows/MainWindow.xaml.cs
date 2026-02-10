using System.Windows;
using System.Windows.Controls;
using AzureCliControlPanel.App.ViewModels;

namespace AzureCliControlPanel.App.Windows;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.NewValue is ResourceGroupNodeViewModel node)
        {
            vm.OnResourceGroupNodeSelected(node);
        }
    }
}
