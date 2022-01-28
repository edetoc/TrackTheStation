using System;
using System.Collections.ObjectModel;
using TrackTheStation.Models;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;


namespace TrackTheStation
{
   
    public sealed partial class CrewPage : Page
    {
        public ObservableCollection<Astronaut> Crew = new ObservableCollection<Astronaut>();

        public CrewPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            LoadCrew();
        }

        public void LoadCrew()
        {

            Crew.Add(new Astronaut(
                 "Hakihido",
                 "Hoshide",
                 "Commander",
                 "JAXA",
                 new Uri("ms-appx:///Assets/Crew/HakihidoHoshide.png")));

            Crew.Add(new Astronaut(
                 "Megan",
                 "McArthur",
                 "Flight Engineer",
                 "NASA",
                 new Uri("ms-appx:///Assets/Crew/MeganMcArthur.png")));

            Crew.Add(new Astronaut(
                 "Thomas",
                 "Pesquet",
                 "Flight Engineer",
                 "ESA",
                 new Uri("ms-appx:///Assets/Crew/ThomasPesquet.png")));

            Crew.Add(new Astronaut(
                "Shane",
                "Kimbrough",
                "Flight Engineer",
                "NASA",
                new Uri("ms-appx:///Assets/Crew/ShaneKimbrough.png")));

            Crew.Add(new Astronaut(
                "Oleg",
                "Novistkiy",
                "Flight Engineer",
                "Roscosmos",
                new Uri("ms-appx:///Assets/Crew/OlegNovitskiy.png")));

            Crew.Add(new Astronaut(
              "Pyotr",
              "Drubrov",
              "Flight Engineer",
              "Roscosmos",
              new Uri("ms-appx:///Assets/Crew/PyotrDubrov.png")));

            Crew.Add(new Astronaut(
              "Mark",
              "Vande Hei",
              "Flight Engineer",
              "NASA",
              new Uri("ms-appx:///Assets/Crew/MarkVandeHei.png")));

        }

        private async void AllExpHyperlinkBtn_Click(object sender, RoutedEventArgs e)
        {
            var uri = new Uri(@"https://en.wikipedia.org/wiki/List_of_International_Space_Station_expeditions");

            // Launch the URI in default browser
            await Windows.System.Launcher.LaunchUriAsync(uri);
        }
    }
}
