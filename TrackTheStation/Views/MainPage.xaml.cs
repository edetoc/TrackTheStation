using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI;
using Windows.UI.Core.Preview;
using Windows.UI.WindowManagement;
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
        // Track open app windows in a Dictionary.
        public static Dictionary<UIContext, AppWindow> AppWindows { get; set; }
            = new Dictionary<UIContext, AppWindow>();

        public MainPage()
        {
            this.InitializeComponent();

            this.Loaded += MainPage_Loaded;
        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            // CloseRequested event is fired when the user closes the app
            SystemNavigationManagerPreview.GetForCurrentView().CloseRequested +=OnCloseRequested;
        }

        private async void OnCloseRequested(object sender, SystemNavigationCloseRequestedPreviewEventArgs e)
        {
            var deferral = e.GetDeferral();

            // Make sure to close all the windows that have opened by the app
            while (MainPage.AppWindows.Count > 0)
            {
                await AppWindows.Values.First().CloseAsync();
            }

            deferral.Complete();
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
