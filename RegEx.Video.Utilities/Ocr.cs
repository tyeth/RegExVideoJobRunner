using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Configuration.Assemblies;
using System.Configuration;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using Accord.Video.FFMPEG;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.MediaServices.Client;

namespace RegEx.Video.Utilities
{
    public static class Ocr
    {
        private static dynamic Jobs;
        //static Regex _regExPSN = new Regex("PSN",
        //    RegexOptions.Compiled | RegexOptions.Multiline);
        static Regex _regExCODE = new Regex(
            "([\\w\\d]{4,5}\\S?\\-\\S?){2}[\\w\\d]{3,5}",
            RegexOptions.Compiled | RegexOptions.Multiline);

        private static readonly string ExeName = Path.GetFileName(Assembly.GetExecutingAssembly().Location);
      //  static readonly string Ffmpeg = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\ffmpeg\\bin\\ffmpeg.exe";
        private static string Ffmpeg =
           "\"C:\\Users\\Tyeth\\AppData\\Local\\JDownloader v2.0\\tools\\Windows\\ffmpeg\\x64\\ffmpeg.exe\"";// Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) + "\\ffmpeg\\bin\\ffmpeg.exe";

        private static List<string> _procsToSpawn = new List<string>();


        // Read values from the App.config file.
        private static readonly string _AADTenantDomain;
        private static readonly string _RESTAPIEndpoint;
        private static readonly string _AMSClientId;
        private static readonly string _AMSClientSecret;

        private static bool NoConsole = false;

        // Field for service context.
        private static CloudMediaContext _context = null;

        static Ocr()
        {
            _AADTenantDomain = SafelyGetConfigValue("AMSAADTenantDomain");
            _RESTAPIEndpoint = SafelyGetConfigValue("AMSRESTAPIEndpoint");
            _AMSClientId = SafelyGetConfigValue("AMSClientId");
            Console.WriteLine($"Tenant{_AADTenantDomain}\nClient{_AMSClientId}");
            _AMSClientSecret = SafelyGetConfigValue("AMSClientSecret");
            if (!System.IO.File.Exists(Ffmpeg))
            {
                Ffmpeg = SafelyGetConfigValue("FFMPEG");
            }
            AzureAdTokenCredentials tokenCredentials =
                new AzureAdTokenCredentials(_AADTenantDomain,
                    new AzureAdClientSymmetricKey(_AMSClientId, _AMSClientSecret),
                    AzureEnvironments.AzureCloudEnvironment);


            _context = new CloudMediaContext(new Uri(_RESTAPIEndpoint), new AzureAdTokenProvider(tokenCredentials));

        }

        public static void SetRegex(string regexCode)
        {
            _regExCODE = new Regex(
                regexCode,
                RegexOptions.Compiled | RegexOptions.Multiline);
        }

        public static IAsset GetAsset(string id)
        {
            return _context.Assets.First(x => x.Id == id);
        }

        public static IJob GetJob(string id)
        {
            return _context.Jobs.First(x => x.Id == id);
        }

        private static string SafelyGetConfigValue(string key)
        {
            string a = "";
            try
            {
                a = Environment.GetEnvironmentVariable(key);
            }
            finally
            {
                if (string.IsNullOrEmpty(a)) a = "NO VALUE";//.AppSettings[key];
            }
            return a;
        }

        public static void Main(string[] args, bool noConsole = false)
        {
            NoConsole = noConsole;
            if (args.Length != 1) throw new ArgumentException("Missing argument, expecting <video>");// <switch>");
            // Run the OCR job.
            var filename = args[0];
            var jsonFilename = filename + ".ocrinput.json";
            var outputFolder = filename + "Output";
            Directory.CreateDirectory(outputFolder);
            if (!File.Exists(filename) || /*!File.Exists(args[1]) ||*/ !Directory.Exists(outputFolder))
                throw new FileNotFoundException();

            // if(args[1]=="/CreateVideoInputJson")
            CreateVideoInputJson(filename, jsonFilename);

            // if(args[1]== "/UploadOcrAsset")
            var uploadAsset = UploadOcrAsset(filename);

            // if(args[1]=="/RunOcrJob")
            var asset = RunOcrJob(uploadAsset, jsonFilename);
            //@"C:\supportFiles\OCR\presentation.mp4",
            //                      @"C:\supportFiles\OCR\config.json");

            // Download the job output asset.
            DownloadAsset(asset, outputFolder);// @"C:\supportFiles\OCR\Output");
                                               //  ProcessOutputJson.ProcessJsonProgram.Main(new string[0]);
            if (!NoConsole) Console.WriteLine("Press enter to exit");
            if (!NoConsole) Console.ReadLine();
        }

        static IAsset RunOcrJob(IAsset asset, string configurationFile)
        {

            IJob job = CreateOcrJob(configurationFile, asset);

            // Use the following event handler to check job progress.  
            job.StateChanged += new EventHandler<JobStateChangedEventArgs>(StateChanged);
            if (!NoConsole) Console.WriteLine($"{DateTime.UtcNow:O} * Launching new Job {job.Name} on Media Services under Task {job.Tasks[0].Name}");

            // Launch the job.
            job.Submit();

            // Check job execution and wait for job to finish.
            Task progressJobTask = job.GetExecutionProgressTask(CancellationToken.None);

            progressJobTask.Wait();

            // If job state is Error, the event handling
            // method for job progress should log errors.  Here we check
            // for error state and exit if needed.
            if (job.State == JobState.Error)
            {
                ErrorDetail error = job.Tasks.First().ErrorDetails.First();
                if (!NoConsole) Console.WriteLine(string.Format("Error: {0}. {1}",
                                                error.Code,
                                                error.Message));
                return null;
            }

            return job.OutputMediaAssets[0];
        }



        public static void CreateVideoInputJson(string _videofile, string _jsonOcrInputfile, string language = "English", string textOrientation = "AutoDetect", string width = "1920", string height = "1080")
        {
            using (var v = new VideoFileReader())
            {
                if ((new FileInfo(_videofile)).Exists == false) throw new FileNotFoundException("Missing file: " + _videofile);
                v.Open(_videofile);
                try { height = v.Height.ToString(); } catch { }
                try { width = v.Width.ToString(); } catch { }
                var jsonString =
                #region json file contents with video width+height inserted
@"  {
        ""Version"":1.0, 
        ""Options"": 
        {
            ""AdvancedOutput"":""true"",
            ""Language"":""" + language + @""", 
            ""TimeInterval"":""00:00:00.5"",
            ""TextOrientation"":""" + textOrientation + @""",
            ""DetectRegions"": [
                    {
                       ""Left"": 1,
                       ""Top"": 1,
                       ""Width"": " + width + @",
                       ""Height"": " + height + @"
                    }
             ]
        }
    }";
                #endregion

                using (var f = new StreamWriter(_jsonOcrInputfile, false, Encoding.Default))
                {
                    f.WriteLine(jsonString);

                }
                v.Close();
            } // end of using VideoFileReader

        }


        public static IJob CreateOcrJob(string configurationFile, IAsset asset)
        {
            if (!NoConsole) Console.WriteLine($"{DateTime.UtcNow:O} * Creating new Job on Media Services");
            // Declare a new job.
            IJob job = _context.Jobs.Create("RegExOCRJob" + asset.Id);

            var task = CreateAzureMediaOcrTask(configurationFile, job, asset);

            if (!NoConsole) Console.WriteLine($"{DateTime.UtcNow:O} * Adding Asset Associations to Job");
            // Specify the input asset.
            task.InputAssets.Add(asset);

            // Add an output asset to contain the results of the job.
            task.OutputAssets.AddNew("RegExOCROutputAsset" + asset.Id, AssetCreationOptions.None);
            return job;
        }

        private static ITask CreateAzureMediaOcrTask(string configurationFile, IJob job, IAsset asset)
        {
            // Get a reference to Azure Media OCR.
            string MediaProcessorName = "Azure Media OCR";

            var processor = GetLatestMediaProcessorByName(MediaProcessorName);

            // Read configuration from the specified file.
            string configuration = System.IO.File.ReadAllText(configurationFile,Encoding.UTF8);

            // Create a task with the encoding details, using a string preset.
            ITask task = job.Tasks.AddNew("RegExOCRTask" + asset.Id,
                processor,
                configuration,
                TaskOptions.None);
            return task;
        }

        public static IAsset UploadOcrAsset(string inputMediaFilePath)
        {
            if (!NoConsole) Console.WriteLine($"{DateTime.UtcNow:O} * Uploading Asset to Media Services");
            if (new FileInfo(inputMediaFilePath).Exists == false) throw new FileNotFoundException("Missing file: " + inputMediaFilePath);

            // Create an asset and upload the input media file to storage.
            IAsset asset = CreateAssetAndUploadSingleFile(inputMediaFilePath,
                "RegExOCRInputAsset" + inputMediaFilePath.Replace('/', 'A').Replace('\\', 'A'),
                AssetCreationOptions.None);
            Console.WriteLine($"{DateTime.UtcNow:O} * Finished uploading asset {asset.Id}");
            return asset;
        }

        static IAsset CreateAssetAndUploadSingleFile(string filePath, string assetName, AssetCreationOptions options)
        {
            IAsset asset = _context.Assets.Create(assetName, options);

            var assetFile = asset.AssetFiles.Create(Path.GetFileName(filePath));
            assetFile.Upload(filePath);

            return asset;
        }

        public static void DownloadAsset(IAsset asset, string outputDirectory)
        {
            foreach (IAssetFile file in asset.AssetFiles)
            {
                file.Download(Path.Combine(outputDirectory, file.Name));
            }
        }

        static IMediaProcessor GetLatestMediaProcessorByName(string mediaProcessorName)
        {
            var processor = _context.MediaProcessors
                .Where(p => p.Name == mediaProcessorName)
                .ToList()
                .OrderBy(p => new Version(p.Version))
                .LastOrDefault();

            if (processor == null)
                throw new ArgumentException(string.Format("Unknown media processor",
                                                           mediaProcessorName));

            return processor;
        }

        static private void StateChanged(object sender, JobStateChangedEventArgs e)
        {
            if (!NoConsole) Console.WriteLine("{DateTime.UtcNow:O} * Job state changed event:");
            if (!NoConsole) Console.WriteLine("  Previous state: " + e.PreviousState);
            if (!NoConsole) Console.WriteLine("  Current state: " + e.CurrentState);

            switch (e.CurrentState)
            {
                case JobState.Finished:
                    if (!NoConsole) Console.WriteLine();
                    if (!NoConsole) Console.WriteLine("Job is finished.");
                    if (!NoConsole) Console.WriteLine();
                    break;
                case JobState.Canceling:
                case JobState.Queued:
                case JobState.Scheduled:
                case JobState.Processing:
                    if (!NoConsole) Console.WriteLine("Please wait...\n");
                    break;
                case JobState.Canceled:
                case JobState.Error:
                    // Cast sender as a job.
                    IJob job = (IJob)sender;
                    // Display or log error details as needed.
                    // LogJobStop(job.Id);
                    break;
                default:
                    break;
            }
        }



        public static void ProcessOutputs(string _videofile, bool needJpegs, bool needVideo, List<JToken> matches,  string clipDuration = "3", string extension = "")
        {
            string uNow = $"{DateTime.UtcNow:O}".Replace(":", "");


            long i = 1;
            foreach (dynamic item in matches)
            {
                double start = item.start;
                double end = item.end;
                double timescale = item.timescale;
                var dt = TimeSpan.FromMilliseconds(Math.Floor((start * 1000) / timescale));//.FromSeconds(Math.Floor(start / timescale));
                var t = dt.ToString("g");
                var de = TimeSpan.FromMilliseconds(Math.Ceiling((1000 * end) / timescale));
                var tso = de - dt;
                var tsot = tso.TotalSeconds;
                TimeSpan ocrDuration = TimeSpan.FromSeconds(3);
                try
                {
                    if(clipDuration.Contains(':'))ocrDuration=TimeSpan.Parse(clipDuration);
                }
                catch(Exception e){}
                if (tsot < 3) tsot = ocrDuration.TotalSeconds; //make min_vid_clip parameter
                var origName = Path.GetFileNameWithoutExtension(_videofile);
                var newSnippetFile = _videofile.Replace(origName ?? string.Empty, $"out_{uNow}_{i}_{origName}" + 
                                                                                  ((extension.Length < 0 && !extension.StartsWith(".")) ? "." + extension : extension)
                                                       );
                var acceleration = ""; // -hwaccel cuvid
#if DEBUG
                acceleration = "-threads 8 ";
#endif
                if (needVideo)
                {
                    var args = $"{acceleration}-i {_videofile} -c:av copy -ss {t} -t {tsot} {newSnippetFile}";
                    _procsToSpawn.Add(args);
                }

                if (needJpegs)
                {
                    var args = $"{acceleration}-ss {t} -i {_videofile} -qscale:v 4 -frames:v 1 -huffman optimal {newSnippetFile}.jpg";
                    _procsToSpawn.Add(args);
                }
                using (var f = File.OpenWrite(newSnippetFile + ".txt"))
                {
                    using (var bw = new BinaryWriter(f))
                    {
                        var str = ($"\r\n{uNow} {item.text} top:{item.top} left:{item.left} width:{item.width} height:{item.height}\r\n");
                        bw.Write(str);
                        bw.Flush();
                    }

                }


                i++;
            }

            Console.WriteLine($"{DateTime.UtcNow:O} *** Processed {i - 1} records.");
            SpawnProcesses();
        }

        private static void SpawnProcesses()
        {
            Console.WriteLine("Spawning processes...");
            var c = 1;
            foreach (var item in _procsToSpawn)
            {
                var p = Process.Start(Ffmpeg, item);
                if (p == null) continue;
                Console.Write(
                    $"{DateTime.UtcNow:O} *** Awaiting finish of process #{c}/{_procsToSpawn.Count} handle:{p.Handle} [Args: ffmpeg {item} ]\nChecking every 500ms...");
                do
                {
                    Thread.Sleep(500);
                    Console.Write(".");
                } while (!p.HasExited);

                Console.WriteLine($"{DateTime.UtcNow:O} Spawned process has finished - Code {p.ExitCode}");
                if (p.ExitCode != 0)
                {
                    try
                    {
 Console.WriteLine($"{DateTime.UtcNow:O} STDOUT for errored process:{p.StandardOutput}");
                    Console.WriteLine($"{DateTime.UtcNow:O} STDERR for errored process:{p.StandardError}");
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{DateTime.UtcNow:O} * No output available for process, try manually running the commandline listed above.");
                    }
                }
                c++;
            }
        }

        public static List<JToken> ExtractRegExMatches(string _jsonOcrOutputfile)
        {
            var matches = new List<JToken>();
            var fileStream = new FileStream(_jsonOcrOutputfile, FileMode.Open);
            var streamJson = new StreamReader(fileStream);
            var strJson = streamJson.ReadToEnd();
            var isMatch = _regExCODE.IsMatch(strJson);
            if (isMatch)
            {
                dynamic jsonVal = JValue.Parse(strJson);


                foreach (dynamic item in jsonVal.fragments)
                {
                    if (item.events == null) continue;
                    int counter = 0;
                    foreach (dynamic evtArray in item.events)
                    {

                        if (evtArray == null) continue;
                        foreach (dynamic evt in evtArray)
                        {
                            var starttime = (item.start) + ((item.interval) * counter);
                            var endtime = starttime + ((item.interval));// * (counter + 1));
                            counter = counter + 1;
                            Console.WriteLine($"{DateTime.UtcNow:O} Counter: {counter}");
                            if (evt.region == null) continue;
                            if (evt.region.lines == null) continue;

                            foreach (dynamic lineOfText in evt.region.lines)
                            {
                                if (lineOfText.text != null)
                                {
                                    if (_regExCODE.IsMatch(lineOfText.text.ToString()))
                                    {
                                        lineOfText.start = starttime;
                                        lineOfText.end = endtime;
                                        lineOfText.timescale = jsonVal.timescale;
                                        matches.Add(lineOfText);


                                        Console.ForegroundColor = ConsoleColor.Cyan;
                                        Console.WriteLine(
                                            $"Start: {starttime} End: {endtime}  " +
                                            JsonConvert.SerializeObject(lineOfText));
                                        Console.Beep(620, 30);

                                        #region json example
                                        //  "version": 1,
                                        // "timescale": 90000,
                                        // "offset": 0,
                                        // "framerate": 60,
                                        // "width": 1920,
                                        // "height": 1080,
                                        // "totalDuration": 39469500,
                                        // "fragments": [
                                        // {
                                        //   "start": 0,
                                        //   "duration": 270000,
                                        //   "interval": 135000,
                                        //   "events": [
                                        //    [
                                        //      {
                                        //          "region": {
                                        //              "language": "English",
                                        //              "orientation": "Up",
                                        //              "lines": [
                                        //              {
                                        //                  "text":
                                        #endregion json example
                                    }

                                }
                            }
                        }
                    }
                }

            }

            return matches;
        }


    }
}
