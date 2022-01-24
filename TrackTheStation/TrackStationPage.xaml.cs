using Microsoft.Toolkit.Uwp.Connectivity;
using One_Sgp4;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.System.Threading;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Maps;
using Windows.UI.Xaml.Navigation;
using Windows.Web.Http;
using Windows.Web.Http.Filters;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=234238

namespace TrackTheStation
{

    public class ISSPosInfo
    {
        public Coordinate coord { get; set; }
        public double speed { get; set; }
    }

    public sealed partial class TrackStationPage : Page
    {
      
        private DateTime _startUtc;

        const int OrbitPeriodInMinutes = 90;
        const double ONE_MINUTE_STEP = 1;
        const double THREE_SECONDS_STEP_IN_MIN = 0.05;

        private Tle _ISSTLE;

        private MapIcon _ISSIcon;
        private MapPolyline _currentOrbitPath;
        private MapPolyline _nextOrbitPath;

        private MapElementsLayer _orbitsLayer;
        private MapElementsLayer _issLayer;


        public TrackStationPage()
        {

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

            this.InitializeComponent();

            App.Current.Resuming += Current_Resuming;

        }


        private async Task<bool> TryGetTLESets()
        {

            HttpClient httpClient;
            CancellationTokenSource cts;
            HttpBaseProtocolFilter filter = new HttpBaseProtocolFilter();
            filter.CacheControl.WriteBehavior = HttpCacheWriteBehavior.NoCache; // Do not cache the http response

            httpClient = new HttpClient(filter);
            cts = new CancellationTokenSource();

            //Uri uri = new Uri("https://www.celestrak.com/NORAD/elements/stations.txt");

            Uri uri = new Uri("https://celestrak.com/satcat/tle.php?CATNR=25544");



            bool needToCheckCache = false;
            bool result = false;

            if (NetworkHelper.Instance.ConnectionInformation.IsInternetAvailable)
            {

                try
                {

                    var response = await httpClient.GetStringAsync(uri).AsTask(cts.Token);

                    /*

                    <!doctype html>

                    <html>

                    <head>

                    <title>TLE for NORAD Catalog Number 25544</title>

                    </head>

                    <body>

                        <pre>
                        ISS (ZARYA)             
                        1 25544U 98067A   19188.35011131  .00001099  00000-0  26459-4 0  9991
                        2 25544  51.6438 261.2359 0007187 115.2183 334.7816 15.50962949178380

                        </pre>
	
                    </body>

                    </html>


                    */



                    //var document = parser.Parse(response);

                    //var preTag = document.QuerySelector("pre");  // we want content within the pre tag (see above)

                    StorageFolder storageFolder = ApplicationData.Current.LocalFolder;

                    StorageFile stationsFile = await storageFolder.CreateFileAsync("stations.txt",
                                                                                    CreationCollisionOption.ReplaceExisting);

                    //await FileIO.WriteTextAsync(stationsFile, response);

                    await FileIO.WriteTextAsync(stationsFile, response);

                    result = true;

                }
                catch (TaskCanceledException)
                {
                    //rootPage.NotifyUser("Request canceled.", NotifyType.ErrorMessage);
                }
                catch (Exception ex)
                {
                    //rootPage.NotifyUser("Error: " + ex.Message, NotifyType.ErrorMessage);

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
                            result = true;
                            break;
                        }

                    }

                }
                catch
                {
                    result = false;
                }

            }

            return result;

        }

        private async void Current_Resuming(object sender, object e)
        {
            await myMapControl.TrySetViewAsync(_ISSIcon.Location);
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            Debug.WriteLine(ApplicationData.Current.LocalFolder.Path);

            var bSuccess = await TryGetTLESets();

            if (!bSuccess)
            {
                // tell user we couldn't fetch TLE or not found in local cache
                throw new FileNotFoundException();
            }

            var tleList = ParserTLE.ParseFile(Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path,
                                                        "stations.txt"));

            _ISSTLE = tleList.Find(x => x.getNoradID() == "25544");  // return null if not found, otherwise the TLE for ISS

            myMapControl.Layers.Add(_orbitsLayer);
            myMapControl.Layers.Add(_issLayer);

            UpdateOrbitPaths(null);
            UpdateISSPosition(null);

            // start periodic timer to refresh ISS location on Map
            var updateISSPositionTimer =
                ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateISSPosition),
                                                    TimeSpan.FromSeconds(3));   // 3 seconds

            var updateOrbitPathsTimer =
                ThreadPoolTimer.CreatePeriodicTimer(new TimerElapsedHandler(UpdateOrbitPaths),
                                                    TimeSpan.FromMinutes(10));

        }

        private async void UpdateOrbitPaths(ThreadPoolTimer timer)
        {

            _startUtc = DateTime.UtcNow;

            var curOrbitSteps = GetOrbitStepsData(_startUtc, _startUtc.AddMinutes(90), ONE_MINUTE_STEP, false);      // current orbit path, step 1 minute            
            var nextOrbitSteps = GetOrbitStepsData(_startUtc.AddMinutes(90), _startUtc.AddMinutes(180), ONE_MINUTE_STEP, false);     // next orbit path

            Coordinate[] curOrbitCoords = curOrbitSteps.Select(step => step.coord).ToArray();
            Coordinate[] nextOrbitCoords = nextOrbitSteps.Select(step => step.coord).ToArray();

            await Dispatcher.RunAsync(CoreDispatcherPriority.High,
                 () =>
                 {

                     _currentOrbitPath = GetGeodesicPath(curOrbitCoords, Colors.Red, false);
                     _nextOrbitPath = GetGeodesicPath(nextOrbitCoords, Colors.Orange, true);

                     _orbitsLayer.MapElements.Clear();

                     _orbitsLayer.MapElements.Add(_currentOrbitPath);
                     _orbitsLayer.MapElements.Add(_nextOrbitPath);

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

                positionsInfo[i].coord = SatFunctions.calcSatSubPoint(t, results[i], Sgp4.wgsConstant.WGS_84);

                if (bNeedSpeed)
                    positionsInfo[i].speed = GetSpeed(results[i]);
            }

            return positionsInfo;

        }

        private double GetSpeed(Sgp4Data data)  // returns speed in km/h
        {
            var vel = data.getVelocityData();

            // x,y,z are km/s
            return 3600 * Math.Sqrt(Math.Pow(Math.Abs(vel.x), 2) +
                                    Math.Pow(Math.Abs(vel.y), 2) +
                                        Math.Pow(Math.Abs(vel.z), 2));

        }

        private async void UpdateISSPosition(ThreadPoolTimer timer)
        {

            var issPosNow = GetOrbitStepsData(DateTime.UtcNow, DateTime.UtcNow, 0.0001666667, true);

            var bgp = new BasicGeoposition();
            bgp.Latitude = issPosNow[0].coord.getLatitude();
            bgp.Longitude = issPosNow[0].coord.getLongitude();

            var gp = new Geopoint(bgp);

            await Dispatcher.RunAsync(CoreDispatcherPriority.High,
                   async () =>
                   {
                       _ISSIcon.Location = gp;

                       myTextBlock.Text = issPosNow[0].coord.getHeight().ToString() + " / " +
                                            issPosNow[0].speed.ToString();

                       if (!_issLayer.MapElements.Contains(_ISSIcon))
                       {
                           _issLayer.MapElements.Add(_ISSIcon);

                           await myMapControl.TrySetViewAsync(_ISSIcon.Location);

                       }

                   });

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


        /// <summary>
        /// Takes a list of coordinates and fills in the space between them with accurately 
        /// positioned points to form a Geodesic path.
        /// 
        /// Source: http://alastaira.wordpress.com/?s=geodesic
        /// code below is from http://mapstoolbox.codeplex.com/SourceControl/latest#Microsoft.Maps.Spatialtoolbox/Source/Microsoft.Maps.SpatialToolbox.Core/SpatialTools.cs
        /// </summary>
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


        /// <summary>
        /// Converts an angle that is in degrees to radians. Angle * (PI / 180)
        /// </summary>
        /// <param name="angle">An angle in degrees</param>
        /// <returns>An angle in radians</returns>
        public static double ToRadians(double angle)
        {
            return angle * (Math.PI / 180);
        }

        /// <summary>
        /// Converts an angle that is in radians to degress. Angle * (180 / PI)
        /// </summary>
        /// <param name="angle">An angle in radians</param>
        /// <returns>An angle in degrees</returns>
        public static double ToDegrees(double angle)
        {
            return angle * (180 / Math.PI);
        }

    
    }
}
