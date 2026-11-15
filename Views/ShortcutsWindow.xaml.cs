using System.Windows;
using System.Windows.Input;

namespace DiceChaosDisplay.Views;

public partial class ShortcutsWindow : Window
{
    public ShortcutsWindow()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.F1 or Key.Enter)
        {
            e.Handled = true;
            Close();
        }
    }
}
