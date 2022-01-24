using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;


namespace TrackTheStation
{
   
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private void navView_Loaded(object sender, RoutedEventArgs e)
        {

            var settings = (NavigationViewItem)navView.SettingsItem;
            settings.Content = "Settings";
            settings.Tag = "TrackTheStation.SettingsPage";

            SetCurrentNavigationViewItem(navView.MenuItems.ElementAt(0) as NavigationViewItem);
        }

        private void navView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
          
                SetCurrentNavigationViewItem(args.SelectedItemContainer as NavigationViewItem);
            
        }

        public void SetCurrentNavigationViewItem(NavigationViewItem item)
        {
            if (item == null)
            {
                return;
            }

            if (item.Tag == null)
            {
                return;
            }

            contentFrame.Navigate( Type.GetType(item.Tag.ToString()), item.Content);
            navView.Header = item.Content;

        }

    }
}
