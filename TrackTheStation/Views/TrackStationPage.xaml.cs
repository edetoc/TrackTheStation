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
        

        public TrackStationPage()
        {

            this.InitializeComponent();

        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {

            // Get TLE
            var bSuccess = await TryGetTLESets();

            if (!bSuccess)
            {
                
                var messageDialog = new MessageDialog("Couldn't retrieve the TLE for the space station", "Error TLE");
                await messageDialog.ShowAsync();

                return;
            }

            // Parse TLE. it looks like this :

                  /*

                  Response looks like:

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

            // Display current orbit path and ISS position on the map
            // 
            UpdateISSPosition(null);
            //UpdateOrbitPath(null);

            // start periodic timers to refresh orbit path (every 10mn) and ISS position (every 3s)
            updateISSPositionTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateISSPosition), 
                                                                            TimeSpan.FromSeconds(3));

            //updateOrbitPathsTimer = ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateOrbitPath),
            //                                                                TimeSpan.FromMinutes(10));

        }

        private void GlobeViewCB_Checked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            myMapControl.MapProjection = MapProjection.Globe;
        }

        private void GlobeViewCB_Unchecked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            myMapControl.MapProjection = MapProjection.WebMercator;
        }

        private void LiveStreamCB_Checked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {

            webView.Navigate(new Uri("https://www.ustream.tv/embed/17074538"));
            webView.Visibility = Windows.UI.Xaml.Visibility.Visible;
        }

        private void LiveStreamCB_Unchecked(object sender, Windows.UI.Xaml.RoutedEventArgs e)
        {
            webView.Visibility = Windows.UI.Xaml.Visibility.Collapsed;
            webView.Navigate(new Uri("about:blank"));

        }

        // this code tries to retrieve the TLE for the ISS from Internet, or from local cache (if no Internet)
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


        // Draw the orbit path for the next 90 minutes
        //private async void UpdateOrbitPath(ThreadPoolTimer timer)
        //{

        //    var curOrbitSteps =  // TO DO 
        //    Coordinate[] curOrbitCoords = curOrbitSteps.Select(step => step.Coord).ToArray();

        //    await Dispatcher.RunAsync(CoreDispatcherPriority.High, () =>
        //          {

        //              _currentOrbitPath = GetGeodesicPath(curOrbitCoords, Colors.Red, false);

        //              _orbitsLayer.MapElements.Clear();

        //              _orbitsLayer.MapElements.Add(_currentOrbitPath);

        //          });

        //}

        // Update ISS current position
        private async void UpdateISSPosition(ThreadPoolTimer timer)
        {

            var issPosNow = GetOrbitStepsData(DateTime.UtcNow, DateTime.UtcNow, 0.0001666667, true);

            var bgp = new BasicGeoposition();
            bgp.Latitude = issPosNow[0].Coord.getLatitude();
            bgp.Longitude = issPosNow[0].Coord.getLongitude();

            var gp = new Geopoint(bgp);

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

        private ISSPosInfo[] GetOrbitStepsData(DateTime startUtc, DateTime endUtc, double step /* in minutes */, bool bNeedSpeed)
        {

            var sgp4Propagator = new Sgp4(_ISSTLE, Sgp4.wgsConstant.WGS_84); // (1 = WGS84; 0 = WGS72)

            EpochTime startTime = new EpochTime(startUtc);
            EpochTime stopTime = new EpochTime(endUtc);

            sgp4Propagator.runSgp4Cal(startTime, stopTime, step);  // one point every step

            List<Sgp4Data> results = new List<Sgp4Data>();
            results = sgp4Propagator.getResults();

            var positionsInfo = new ISSPosInfo[results.Count];

            for (int i = 0; i < results.Count; i++)
            {

                EpochTime t = new EpochTime(startUtc.AddMinutes(i * step));   // 1mn

                positionsInfo[i] = new ISSPosInfo();

                positionsInfo[i].Coord = SatFunctions.calcSatSubPoint(t, results[i], Sgp4.wgsConstant.WGS_84);

                if (bNeedSpeed)
                    positionsInfo[i].Speed = GetSpeed(results[i]);
            }

            return positionsInfo;

        }

        private double GetSpeed(Sgp4Data data)  // returns speed in km/h
        {
            var vel = data.getVelocityData();

            double speed;

            // x,y,z are km/s
            speed = 3600 * Math.Sqrt(Math.Pow(Math.Abs(vel.x), 2) +
                                    Math.Pow(Math.Abs(vel.y), 2) +
                                        Math.Pow(Math.Abs(vel.z), 2));

            // TO DO : perform the above calculation using an Azure function

            return speed;

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
        /// Source: http://alastaira.wordpress.com/?s=geodesic
        /// code below is from http://mapstoolbox.codeplex.com/SourceControl/latest#Microsoft.Maps.Spatialtoolbox/Source/Microsoft.Maps.SpatialToolbox.Core/SpatialTools.cs
        
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
