using System.Windows;
using System.Windows.Controls;

namespace SalesforceDebugAnalyzer.Views.Tabs;

public partial class SecurityTab : UserControl
{
    public SecurityTab()
    {
        InitializeComponent();
    }

    private void SectionToggle_Checked(object sender, RoutedEventArgs e)
    {
        if (ShieldContent == null || PiiContent == null) return;

        if (ShieldToggle.IsChecked == true)
        {
            ShieldContent.Visibility = Visibility.Visible;
            PiiContent.Visibility = Visibility.Collapsed;
        }
        else
        {
            ShieldContent.Visibility = Visibility.Collapsed;
            PiiContent.Visibility = Visibility.Visible;
        }
    }
}
