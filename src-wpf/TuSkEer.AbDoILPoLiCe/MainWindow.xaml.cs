using System;
using System.ComponentModel;
using System.Windows;
using TuSkEer.AbDoILPoLiCe.ViewModels;

namespace TuSkEer.AbDoILPoLiCe;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly Func<bool> _isApplicationExiting;

    public MainWindow(MainViewModel viewModel, Func<bool> isApplicationExiting)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _isApplicationExiting = isApplicationExiting;
        DataContext = _viewModel;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (_isApplicationExiting())
        {
            return;
        }

        if (_viewModel.MinimizeToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
        }
        else
        {
            e.Cancel = true;
            ((App)Application.Current).ExitApplicationCompletely();
        }
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeRestore_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void ContactDeveloper_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://wa.me/201141706613") { UseShellExecute = true });
        }
        catch { }
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        SettingsMenu.IsOpen = !SettingsMenu.IsOpen;
    }

    private void CloseToTray_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.MinimizeToTrayOnClose)
        {
            Hide();
        }
        else
        {
            ((App)Application.Current).ExitApplicationCompletely();
        }
    }

    private void ExitCompletely_Click(object sender, RoutedEventArgs e)
    {
        ((App)Application.Current).ExitApplicationCompletely();
    }

    private void BackdropAcrylic_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BackdropMaterial = Models.BackdropMaterialMode.Acrylic;
    }

    private void BackdropMica_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BackdropMaterial = Models.BackdropMaterialMode.Mica;
    }

    private void BackdropMidnight_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.BackdropMaterial = Models.BackdropMaterialMode.MidnightGlass;
    }
}
