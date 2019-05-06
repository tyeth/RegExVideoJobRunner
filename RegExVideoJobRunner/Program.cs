using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq.JsonPath;
using Microsoft.WindowsAzure.MediaServices;
using Microsoft.WindowsAzure.MediaServices.Client;
using RegEx.Video;
using RegEx.Video.Models;
using RegEx.Video.Utilities;


namespace RegExVideoJobRunner
{
    class Program
    {
        static void Main(string[] args)
        {
            //EF Core import done.


            // Set ffmpeg to use http://www.ffmpeg.org/ffmpeg-filters.html#Examples-82 for pattern highlighting in video
            while (true)
            {

                using (var dbContext = new RegExVideoDbContext())
                {


                    var jobs = dbContext.Jobs.Where(j => true).Select(j => j).ToList();
                    foreach (var job in jobs)
                    {
                        Console.WriteLine($"{DateTime.UtcNow:O} * Examining job id:{job.Id} {getJobTypeStr(job)}:{job.Status}");
                        bool skipOut = false;
                        RegExVideoJob precursor = null;
                        if (!string.IsNullOrEmpty(job.Precursor)
                        )
                        {
                            precursor = jobs.FirstOrDefault(x => x.Id == job.Precursor);
                            if (precursor != null)
                            {
                                var precursorJson = JsonConvert.DeserializeObject<RegExJobDTO>(precursor.OutputJson,
                                    new JsonSerializerSettings()
                                    {
                                        MissingMemberHandling = MissingMemberHandling.Ignore,
                                        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                        MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                                    });

                                if (precursor.Status.StartsWith("Finished") || precursor.Status.StartsWith("Purg") || precursor.Status.StartsWith("Done")) skipOut = false;
                                if (precursor.Status.StartsWith("ERROR") && precursor.IsCloudJobFinished)
                                {
                                    Console.WriteLine($"{DateTime.UtcNow:O} * Failing current job due to failed precursor ({precursorJson.Error})");
                                    job.Status = $"ERROR: Precursor failed: {precursorJson.Error}";
                                    job.IsCloudJobFinished = true;
                                    dbContext.SaveChanges();
                                    continue;
                                }
                                //var ageOfJob = DateTime.UtcNow - precursorJson.LastUpdated ;
                                //if (skipOut && ageOfJob.TotalMinutes >= 25)
                                //{
                                //    // job timed out
                                //    skipOut = false;
                                //}
                                if (precursor.Status.StartsWith("Pending") || precursor.Status.StartsWith("Ready") ||
                                    precursor.Status.StartsWith("Started"))
                                {
                                    skipOut = true;
                                }

                            }
                        }
                        if (!skipOut && (job.Status.StartsWith("Finished") || job.Status.StartsWith("Purg") || job.Status.StartsWith("ERROR") || job.Status.StartsWith("Done"))) skipOut = true;

                        if (skipOut)
                        {
                            Console.WriteLine(
                                $"{DateTime.UtcNow:O} * Skipping Job {job.Id} status:{job.Status} (precursor job {job.Precursor} status:{precursor?.Status})");
                            continue;
                        }

                        switch (job.JobType)
                        {

                            case RegExJobType.OcrUploadVideoJob:

                                var json1 = JsonConvert.DeserializeObject<JSONOcrUploadVideoDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                                job.Status = "Started";
                                json1.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json1);
                                dbContext.SaveChanges();
                                try
                                {

                                    var asset1 = Ocr.UploadOcrAsset(json1.input.FilePath);
                                    json1.LastUpdated = DateTime.UtcNow;
                                    json1.IAssetId = asset1.Id;
                                    json1.Error = "";
                                    job.IsUploadedToCloud = true;
                                    job.IsCloudJobFinished = true;
                                    job.Status = "Finished";

                                }
                                catch (FileNotFoundException fe)
                                {
                                    job.Status = "ERROR";
                                    Console.WriteLine(string.Format("{1:O} * Error: {0}",
                                        fe.Message, DateTime.UtcNow));
                                    //return null;
                                    job.IsUploadedToCloud = false;
                                    job.IsCloudJobFinished = true;
                                    json1.Error = fe.Message;
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(
                                        $"{DateTime.UtcNow:O} * ERROR - Will Retry next cycle. [Msg: {e.Message}]");
                                    job.Status = "ERROR";
                                    json1.LastUpdated = DateTime.UtcNow;
                                    json1.Error = e.Message;
                                    job.IsCloudJobFinished = false;
                                }
                                finally
                                {
                                    job.OutputJson = JsonConvert.SerializeObject(json1);
                                    dbContext.SaveChanges();
                                }
                                break;
                            case RegExJobType.OcrCreateMediaAnalyticsJob:
                                //
                                var json2 = JsonConvert.DeserializeObject<JSONOcrCreateJobDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                                job.Status = "Started";
                                json2.LastUpdated = DateTime.UtcNow;
                                json2.IAssetId = (JsonConvert.DeserializeObject<JSONOcrUploadVideoDTO>(precursor.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore })).IAssetId;
                                job.OutputJson = JsonConvert.SerializeObject(json2);
                                dbContext.SaveChanges();
                                try
                                {
                                    var configurationFile = json2.input.FilePath + ".ocrinput.json";
                                    Ocr.CreateVideoInputJson(json2.input.FilePath, configurationFile);
                                    var iasset = Ocr.GetAsset(json2.IAssetId);
                                    var job2 = Ocr.CreateOcrJob(configurationFile, iasset);
                                    job2.Submit();
                                    json2.IJobId = job2.Id;
                                    json2.ITaskId = job2.Tasks[0].Id;
                                    json2.LastUpdated = DateTime.UtcNow;
                                    job.OutputJson = JsonConvert.SerializeObject(json2);
                                    job.Status = "Finished";
                                    dbContext.SaveChanges();

                                }
                                catch (FileNotFoundException fe)
                                {
                                    job.Status = "ERROR";
                                    Console.WriteLine(string.Format("{1:O} * Error: {0}",
                                        fe.Message, DateTime.UtcNow));
                                    //return null;
                                    job.IsUploadedToCloud = false;
                                    job.IsCloudJobFinished = true;
                                    json2.Error = fe.Message;
                                }
                                finally
                                {
                                    job.OutputJson = JsonConvert.SerializeObject(json2);
                                    dbContext.SaveChanges();
                                }
                                break;
                            case RegExJobType.OcrDownloadResultJob:

                                //
                                var json3 = JsonConvert.DeserializeObject<JSONOcrDownloadResultsDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                                var prejson = (JsonConvert.DeserializeObject<JSONOcrUploadVideoDTO>(precursor.OutputJson,
                                     new JsonSerializerSettings()
                                     {
                                         MissingMemberHandling = MissingMemberHandling.Ignore,
                                         ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
                                         MetadataPropertyHandling = MetadataPropertyHandling.Ignore
                                     }));
                                json3.IJobId = prejson.IJobId;
                                json3.IAssetId = prejson.IAssetId;
                                json3.ITaskId = prejson.ITaskId;
                                job.Status = "Started";
                                json3.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json3);
                                dbContext.SaveChanges();
                                var job3 = Ocr.GetJob(json3.IJobId);
                                Task progressJobTask = job3.GetExecutionProgressTask(CancellationToken.None);

                                progressJobTask.Wait();

                                // If job state is Error, the event handling
                                // method for job progress should log errors.  Here we check
                                // for error state and exit if needed.
                                if (job3.State == JobState.Error)
                                {
                                    ErrorDetail error = job3.Tasks.First().ErrorDetails.First();
                                    Console.WriteLine(string.Format("{0:O} * Error: {1}. {2}",
                                        DateTime.UtcNow,
                                        error.Code,
                                        error.Message));
                                    //return null;
                                    job.Status = "ERROR";
                                    job.IsCloudJobFinished = true;
                                    dbContext.SaveChanges();
                                    break;
                                }

                                var outAsset = job3.OutputMediaAssets[0];
                                Directory.CreateDirectory(json3.OutputFileName);
                                Ocr.DownloadAsset(outAsset, json3.OutputFileName);
                                json3.IAssetId = outAsset.Id;
                                json3.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json3);
                                job.IsCloudJobFinished = true;
                                job.Status = "Finished";
                                dbContext.SaveChanges();
                                break;
                            case RegExJobType.OcrCreateLocalJpegSnippetJob:
                                //
                                var json4 = JsonConvert.DeserializeObject<JSONOcrCreateSnippetDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                                job.Status = "Started";
                                json4.IAssetId = (JsonConvert.DeserializeObject<JSONOcrDownloadResultsDTO>(precursor.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore })).IAssetId;
                                json4.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json4);
                                dbContext.SaveChanges();
                                var outAsset4 = Ocr.GetAsset(json4.IAssetId);
                                Directory.CreateDirectory(json4.OutputFileName);
                                var oFile = Path.Combine(json4.OutputFileName, outAsset4.AssetFiles.First().Name);
                                Ocr.SetRegex(json4.Pattern);
                                var matches = Ocr.ExtractRegExMatches(oFile);
                                Ocr.ProcessOutputs(json4.OutputFileName.Replace(".output.json", ""), needJpegs: true, needVideo: false, matches: matches, clipDuration: json4.clipDuration);
                                json4.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json4);
                                job.IsCloudJobFinished = true;
                                job.Status = "Finished";
                                dbContext.SaveChanges();
                                //TODO: Add log of finished processing of job to all switch statements or after block and therefore successful save.
                                break;
                            case RegExJobType.OcrCreateLocalVideoSnippetJob:
                                //
                                var json5 = JsonConvert.DeserializeObject<JSONOcrCreateSnippetDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                                job.Status = "Started";
                                json5.IAssetId = (JsonConvert.DeserializeObject<JSONOcrDownloadResultsDTO>(precursor.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore })).IAssetId;
                                json5.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json5);
                                dbContext.SaveChanges();
                                var outAsset5 = Ocr.GetAsset(json5.IAssetId);
                                Directory.CreateDirectory(json5.OutputFileName);
                                var oFile5 = Path.Combine(json5.OutputFileName, outAsset5.AssetFiles.First().Name);
                                Ocr.SetRegex(json5.Pattern);
                                var matches5 = Ocr.ExtractRegExMatches(oFile5);
                                if (matches5.Count == 0) { Console.WriteLine("No Regex Matches Found!"); }
                                Ocr.ProcessOutputs(json5.OutputFileName.Replace(".output.json", ""), needJpegs: false, needVideo: true, matches: matches5, clipDuration: json5.clipDuration, extension: json5.OriginalVideoFileExtension);
                                json5.LastUpdated = DateTime.UtcNow;
                                job.OutputJson = JsonConvert.SerializeObject(json5);
                                job.IsCloudJobFinished = true;
                                job.Status = "Finished";
                                dbContext.SaveChanges();
                                break;
                            default:
                                break;
                        }

                    }

                }
                Console.WriteLine($"{DateTime.UtcNow:O} * Waiting {(60 - DateTime.Now.Second)}s to freshen jobs and start next processing cycle...");
                // Run jobs at next minute.
                Thread.Sleep((60 - DateTime.Now.Second) * 1000);

            }

        }

        private static string getJobTypeStr(RegExVideoJob job) =>
            Enum.GetName(typeof(RegExJobType), job.JobType);


        // ## Example Jobs json in db.
        //{
        //  "0-Help": {
        //    "SqlOrder": "JobType"
        //  },
        //  "1-2": {
        //    "input": {
        //      "Id": "47024a16-d1a4-440c-999e-25a239e46fe6",
        //      "FileNameFromUpload": "20190501_074846.mp4",
        //      "FilePath": "C:\\Users\\Tyeth\\AppData\\Local\\Temp\\tmp8819.tmp",
        //      "UserId": "dfc01781-23f6-48e7-91ab-efb03bd9aa33",
        //      "UploadTimeStamp": "2019-05-05T16:56:13.0215057"
        //    },
        //    "IAssetId": "nb:cid:UUID:959c05e1-eddc-4e0c-ba7e-83387f820385",
        //    "IJobId": "nb:jid:UUID:563831ff-0300-abc2-04e5-f1e96f571393",
        //    "ITaskId": "nb:tid:UUID:563831ff-0300-abc2-04e6-f1e96f571393",
        //    "Pattern": "(?:[\\w]{3,16}\\S?\\-\\S?){1,12}[\\w]{3,16}",
        //    "Error": null,
        //    "LastUpdated": "2019-05-05T16:59:01.8538792Z"
        //  },
        //  "2-3": {
        //    "OutputFileName": "C:\\Users\\Tyeth\\AppData\\Local\\Temp\\tmp8819.tmp.output.json",
        //    "IAssetId": "nb:cid:UUID:adec2ff9-5e98-45f1-867b-c56b2ca0b6ed",
        //    "IJobId": "nb:jid:UUID:563831ff-0300-abc2-04e5-f1e96f571393",
        //    "ITaskId": "nb:tid:UUID:563831ff-0300-abc2-04e6-f1e96f571393",
        //    "Error": null,
        //    "LastUpdated": "2019-05-05T17:04:41.1053265Z"
        //  },
        //  "3-1": {
        //    "input": {
        //      "Id": "47024a16-d1a4-440c-999e-25a239e46fe6",
        //      "FileNameFromUpload": "20190501_074846.mp4",
        //      "FilePath": "C:\\Users\\Tyeth\\AppData\\Local\\Temp\\tmp8819.tmp",
        //      "UserId": "dfc01781-23f6-48e7-91ab-efb03bd9aa33",
        //      "UploadTimeStamp": "2019-05-05T16:56:13.0215057"
        //    },
        //    "IAssetId": "nb:cid:UUID:959c05e1-eddc-4e0c-ba7e-83387f820385",
        //    "IJobId": null,
        //    "ITaskId": null,
        //    "Pattern": "(?:[\\w]{3,16}\\S?\\-\\S?){1,12}[\\w]{3,16}",
        //    "Error": "",
        //    "LastUpdated": "2019-05-05T16:57:54.4161806Z"
        //  },
        //  "4-5": {
        //    "OutputFileName": "C:\\Users\\Tyeth\\AppData\\Local\\Temp\\tmp8819.tmp.output.json",
        //    "OriginalVideoFileExtension": "mp4",
        //    "isVideo": true,
        //    "IAssetId": "nb:cid:UUID:adec2ff9-5e98-45f1-867b-c56b2ca0b6ed",
        //    "IJobId": null,
        //    "ITaskId": null,
        //    "clipDuration": "0:00:02",
        //    "Error": null,
        //    "LastUpdated": "2019-05-05T17:04:46.2121932Z"
        //  }
        //}

    }
}
