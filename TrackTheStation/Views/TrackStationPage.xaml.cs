using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Navigation;

using Microsoft.Toolkit.Uwp.Connectivity;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using One_Sgp4;
using TrackTheStation.Models;
using Windows.UI.WindowManagement;
using TrackTheStation.Views;
using Windows.UI.Xaml.Hosting;

namespace TrackTheStation
{

    public sealed partial class TrackStationPage : Page
    {
      
        private Tle _ISSTLE;    // https://en.wikipedia.org/wiki/Two-line_element_set

        private MapIcon _ISSIcon;
        private MapPolyline _currentOrbitPath;        

        private MapElementsLayer _orbitsLayer;
        private MapElementsLayer _issLayer;

        ThreadPoolTimer updateISSPositionTimer;
        ThreadPoolTimer updateOrbitPathsTimer;

        const double STEP_IN_MINUTES = 1;

        const string FUNCTION_URL_STRING =  "<FILL HERE>";
        const string FUNCTION_NAME=         "<FILL HERE>";
        

        public TrackStationPage()
        {

            this.InitializeComponent();

        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {

            GlobeViewCB.Tapped += GlobeViewCB_Tapped;
            LiveStreamCB.Tapped += LiveStreamCB_Tapped;

            // Get TLE
            var bSuccess = await TryGetTLESets();

            if (!bSuccess)
            {
                
                var messageDialog = new MessageDialog("Couldn't retrieve the TLE for the space station", "Error TLE");
                await messageDialog.ShowAsync();

                return;
            }

            // Parse TLE. a TLE looks like this :

                  /*

                  ISS (ZARYA)             
                  1 25544U 98067A   22024.46959583  .00005654  00000-0  10839-3 0  9993
                  2 25544  51.6445 328.3082 0006842  57.4089  51.6405 15.49611941322892

                  */

            var tleList = ParserTLE.ParseFile(Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "stations.txt"));

            _ISSTLE = tleList.Find(x => x.getNoradID() == "25544");  // return null if not found, otherwise the TLE for ISS


            // Initializations for Map

            _ISSIcon = new MapIcon();

            _ISSIcon.Image = RandomAccessStreamReference.CreateFromUri(new Uri("ms-appx:///Assets/ISSicon.png"));

            // https://msdn.microsoft.com/en-gb/library/windows/apps/windows.ui.xaml.controls.maps.mapicon.normalizedanchorpoint.aspx
            // 0,0 is upper left corner of the image
            _ISSIcon.NormalizedAnchorPoint = new Point(0.5, 0.5);

            _orbitsLayer = new MapElementsLayer
            {
                ZIndex = 1

            };

            _issLayer = new MapElementsLayer
            {
                ZIndex = 2

            };

            myMapControl.Layers.Add(_orbitsLayer);
            myMapControl.Layers.Add(_issLayer);

            // Display ISS position on the map             
            UpdateISSPosition(null);
          
            // start a periodic timer to refresh orbit path (every 10mn) and ISS position (every 3s)
            updateISSPositionTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateISSPosition), 
                                                                            TimeSpan.FromSeconds(3));
            
            
            // Exercise 2.

            // Display the current orbit path of the Station on the map
            //UpdateOrbitPath(null);

            // start a periodic timer to refresh the Station orbit path (every 10mn) 
            //updateOrbitPathsTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateOrbitPath),
            //                                                                TimeSpan.FromMinutes(10));

        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            // unsuscribe events

            GlobeViewCB.Tapped -= GlobeViewCB_Tapped;
            LiveStreamCB.Tapped -= LiveStreamCB_Tapped;

            // you may need to add something here for Exercise 3 ...

        }

        // this code tries to retrieve the TLE for the ISS from Celestrak.com, or from local cache (if no Internet)
        private async Task<bool> TryGetTLESets()
        {

            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue { NoCache = true };

            bool needToCheckCache = false;
            bool bTLEfound = false;

            if (NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            {

                try
                {

                    var response = await httpClient.GetStringAsync(new Uri("https://celestrak.com/satcat/tle.php?CATNR=25544"));

                    Debug.WriteLine(ApplicationData.Current.LocalFolder.Path);

                    StorageFolder storageFolder = ApplicationData.Current.LocalFolder;

                    StorageFile stationsFile = await storageFolder.CreateFileAsync("stations.txt",
                                                                                    CreationCollisionOption.ReplaceExisting);

                    await FileIO.WriteTextAsync(stationsFile, response);

                    bTLEfound = true;

                }              
                catch (Exception)
                {
                    
                    needToCheckCache = true;
                }
            }
            else
                needToCheckCache = true;

            if (needToCheckCache)
            {

                try
                {
                    StorageFolder appFolder = ApplicationData.Current.LocalFolder;
                    var files = await appFolder.GetFilesAsync();

                    foreach (var f in files)
                    {
                        if (string.Equals(f.Name, "stations.txt"))
                        {
                            bTLEfound = true;
                            break;
                        }

                    }

                }
                catch
                {
                    bTLEfound = false;
                }

            }

            return bTLEfound;

        }

        // Update ISS current location on map
        private async void UpdateISSPosition(ThreadPoolTimer timer)
        {
            // Get current location
            var issPosNow = GetOrbitStepsData(DateTime.UtcNow, DateTime.UtcNow, 0.0001666667, true);

            var bgp = new BasicGeoposition();
            bgp.Latitude = issPosNow[0].Coord.getLatitude();
            bgp.Longitude = issPosNow[0].Coord.getLongitude();

            var gp = new Geopoint(bgp);

            // Update ISS location on Map
            await Dispatcher.RunAsync(CoreDispatcherPriority.High, async () =>
                   {
                       _ISSIcon.Location = gp;

                       AltTB.Text = String.Format("{0:#} km", issPosNow[0].Coord.getHeight());
                       VelocityTB.Text = String.Format("{0:#} km/h", issPosNow[0].Speed);

                       if (!_issLayer.MapElements.Contains(_ISSIcon))
                       {
                           _issLayer.MapElements.Add(_ISSIcon);

                           await myMapControl.TrySetViewAsync(_ISSIcon.Location);

                       }

                   });

        }


        // Draw the orbit path for the next 90 minutes
        //private async void UpdateOrbitPath(ThreadPoolTimer timer)
        //{

        //    var curOrbitSteps =  // Exercise 2: TO DO ... You can find hint from UpdateISSPosition() method
        //    Coordinate[] curOrbitCoords = curOrbitSteps.Select(step => step.Coord).ToArray();

        //    await Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
        //          {

        //              _currentOrbitPath = GetGeodesicPath(curOrbitCoords, Colors.Red, false);

        //              _orbitsLayer.MapElements.Clear();

        //              _orbitsLayer.MapElements.Add(_currentOrbitPath);

        //          });

        //}


        // Returns an array of positions between startUtc and endUtc
        private ISSPosInfo[] GetOrbitStepsData(DateTime startUtc, DateTime endUtc, double step /* in minutes */, bool bNeedSpeed)
        {

            var sgp4Propagator = new Sgp4(_ISSTLE, Sgp4.wgsConstant.WGS_84); // (1 = WGS84; 0 = WGS72)

            EpochTime startTime = new EpochTime(startUtc);
            EpochTime stopTime = new EpochTime(endUtc);

            // use the model propagator to calculate satellite position vector at every single step between start and stop
            // note: the Sgp4Data class holds the calculated position and velocity vectors of the satellite
            sgp4Propagator.runSgp4Cal(startTime, stopTime, step);
                        
            List<Sgp4Data> results = new List<Sgp4Data>();          
            results = sgp4Propagator.getResults();

            var positionsInfo = new ISSPosInfo[results.Count];

            for (int i = 0; i < results.Count; i++)
            {

                EpochTime t = new EpochTime(startUtc.AddMinutes(i * step));   // 1mn

                positionsInfo[i] = new ISSPosInfo();
                
                // calculate Latitude, longitude and height for satellite on earth at given time point and position of the satellite
                positionsInfo[i].Coord = SatFunctions.calcSatSubPoint(t, results[i], Sgp4.wgsConstant.WGS_84);

                if (bNeedSpeed)
                    positionsInfo[i].Speed = GetSpeed(results[i]);
            }

            return positionsInfo;

        }

        private double GetSpeed(Sgp4Data data)  // returns speed in km/h
        {
            var vel = data.getVelocityData();

            double speed=0;

            // x,y,z are km/s
            speed = 3600 * Math.Sqrt(Math.Pow(Math.Abs(vel.x), 2) +
                                    Math.Pow(Math.Abs(vel.y), 2) +
                                        Math.Pow(Math.Abs(vel.z), 2));

            // Exercise 1 : uncomment the Try-Catch block below (and comment the code above) to call the Azure function to retrieve the Speed of the Station
            //              You'll need to change FUNCTION_URL_STRING and FUNCTION_NAME (these const are defined on lines 49 and 50) with your own values

            //try
            //{

            //    StringBuilder uriString = new StringBuilder();

            //    uriString.AppendFormat("https://{0}/api/{1}?x={2}&y={3}&z={4}", FUNCTION_URL_STRING, 
            //                                                                    FUNCTION_NAME, 
            //                                                                    vel.x.ToString(),
            //                                                                    vel.y.ToString(),
            //                                                                    vel.z.ToString());

            //    using (HttpClient client = new HttpClient())
            //    {

            //        using (HttpResponseMessage response = client.GetAsync(new Uri(uriString.ToString())).Result)
            //        {
            //            if (response.IsSuccessStatusCode)
            //            {
            //                using (HttpContent respContent = response.Content)
            //                {
            //                    var tr = respContent.ReadAsStringAsync().Result;
            //                    dynamic azureResponse = JsonConvert.DeserializeObject(tr);
            //                    speed = (double)azureResponse;

            //                }
            //            }

            //        }
            //    }

            //}
            //catch (Exception)
            //{
            //    speed = 0;
            //}


            return speed;

        }

        private void GlobeViewCB_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if ((sender as CheckBox).IsChecked.HasValue)
            {
                myMapControl.MapProjection = ((sender as CheckBox).IsChecked.Value == true) ? MapProjection.Globe : MapProjection.WebMercator;

            }
        }

        private async void LiveStreamCB_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            if ((sender as CheckBox).IsChecked.HasValue)
            {
                if ((sender as CheckBox).IsChecked.Value == true)
                {

                    // Create a new window.
                    AppWindow appWindow = await AppWindow.TryCreateAsync();

                    // Set window dimensions
                    appWindow.RequestSize(new Size(450, 450));
                    
                    // Create a Frame and navigate to the Page you want to show in the new window.
                    Frame appWindowContentFrame = new Frame();
                    appWindowContentFrame.Navigate(typeof(LiveStreamPage));

                    // Attach the XAML content to the window.
                    ElementCompositionPreview.SetAppWindowContent(appWindow, appWindowContentFrame);

                    // Add the new page to the Dictionary using the UIContext as the Key.
                    MainPage.AppWindows.Add(appWindowContentFrame.UIContext, appWindow);
                    appWindow.Title = "Space Station Live stream";

                    // When the window is closed, be sure to release XAML resources
                    // and the reference to the window.                    
                    appWindow.Closed += delegate
                    {
                        MainPage.AppWindows.Remove(appWindowContentFrame.UIContext);
                        appWindowContentFrame.Content = null;
                        appWindow = null;

                        LiveStreamCB.IsChecked = false;
                    };

                    // Show the window.
                    await appWindow.TryShowAsync();

                }
                else
                {
                    while (MainPage.AppWindows.Count > 0)
                    {
                        await MainPage.AppWindows.Values.First().CloseAsync();
                    }
                }
            }
        }

        // Draw the geodesic path of ISS based on an array of geo positions 
        private MapPolyline GetGeodesicPath(Coordinate[] positions, Color color, bool isDashed)
        {
            if (positions == null || positions.Length == 0)
                return null;

            //calculate geodesic path
            var geodesicLocs = ToGeodesic(positions, 32);

            // instantiate MapPolyLine
            var polyline = new MapPolyline();

            // add geopsitions to path
            polyline.Path = new Geopath(geodesicLocs);

            //set appearance of connector line
            polyline.StrokeColor = color;
            polyline.StrokeThickness = 2;
            polyline.StrokeDashed = isDashed;

            return polyline;

        }

        /// Takes a list of coordinates and fills in the space between them with accurately 
        /// positioned points to form a Geodesic path.
        ///         
        /// <param name="coordinates">List of coordinates to work with.</param>
        /// <param name="nodeSize">Number of nodes to insert between each coordinate</param>
        /// <returns>A set of coordinates that for geodesic paths.</returns>
        List<BasicGeoposition> ToGeodesic(Coordinate[] coordinates, int nodeSize)
        {

            List<BasicGeoposition> locs = new List<BasicGeoposition>();

            for (var i = 0; i < coordinates.Length - 1; i++)
            {
                // Convert coordinates from degrees to Radians           
                var lat1 = ToRadians(coordinates[i].getLatitude());
                var lon1 = ToRadians(coordinates[i].getLongitude());
                var lat2 = ToRadians(coordinates[i + 1].getLatitude());
                var lon2 = ToRadians(coordinates[i + 1].getLongitude());

                // Calculate the total extent of the route           
                var d = 2 * Math.Asin(Math.Sqrt(Math.Pow((Math.Sin((lat1 - lat2) / 2)), 2) + Math.Cos(lat1) * Math.Cos(lat2) * Math.Pow((Math.Sin((lon1 - lon2) / 2)), 2)));

                // Calculate positions at fixed intervals along the route
                for (var k = 0; k <= nodeSize; k++)
                {
                    var f = (k / (double)nodeSize);
                    var A = Math.Sin((1 - f) * d) / Math.Sin(d);
                    var B = Math.Sin(f * d) / Math.Sin(d);

                    // Obtain 3D Cartesian coordinates of each point             
                    var x = A * Math.Cos(lat1) * Math.Cos(lon1) + B * Math.Cos(lat2) * Math.Cos(lon2);
                    var y = A * Math.Cos(lat1) * Math.Sin(lon1) + B * Math.Cos(lat2) * Math.Sin(lon2);
                    var z = A * Math.Sin(lat1) + B * Math.Sin(lat2);

                    // Convert these to latitude/longitude             
                    var lat = Math.Atan2(z, Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2)));
                    var lon = Math.Atan2(y, x);

                    // Add this to the array             
                    //locs.Add(new Coordinate(ToDegrees(lat), ToDegrees(lon)));

                    locs.Add(new BasicGeoposition()
                    {
                        Latitude = ToDegrees(lat),
                        Longitude = ToDegrees(lon)
                    });

                }
            }

            return locs;

        }


        
        /// Converts an angle that is in degrees to radians. Angle * (PI / 180)
        
        /// <param name="angle">An angle in degrees</param>
        /// <returns>An angle in radians</returns>
        public static double ToRadians(double angle)
        {
            return angle * (Math.PI / 180);
        }

        
        /// Converts an angle that is in radians to degress. Angle * (180 / PI)
        
        /// <param name="angle">An angle in radians</param>
        /// <returns>An angle in degrees</returns>
        public static double ToDegrees(double angle)
        {
            return angle * (180 / Math.PI);
        }

     
    }
}
