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
using ProcessOutputJson;
using OCR;
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
            while (true)
            {

                var dbContext = new RegExVideoDbContext();
                var jobs = dbContext.Jobs.Where(j => true).Select(j => j).ToList();
                foreach (var job in jobs)
                {
                    bool continu = false;
                    if (!string.IsNullOrEmpty(job.Precursor)
                   )
                    {
                        var precusor = jobs.FirstOrDefault(x => x.Id == job.Precursor);
                        if (precusor != null)
                        {
                            if (JsonConvert.DeserializeObject<RegExJobDTO>(precusor.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore }).LastUpdated - DateTime.UtcNow >= TimeSpan.FromMinutes(5))
                            {
                                // job timed out
                                continu = false;
                            }
                            else if (precusor.Status != "Finished" || !precusor.Status.StartsWith("Purg") || precusor.Status != "Done") continu=true;
                        }
                        else { continu=true; }

                        if (continu)
                        {
                            Console.WriteLine(
                                $"{DateTime.UtcNow:O} * Skipping Job {job.Id} while awaiting precursor job {job.Precursor} (Current status:{precusor.Status})");
                            continue;
                        }
                    }

                    switch (job.JobType)
                    {

                        case RegExJobType.OcrUploadVideoJob:

                            var json1 = JsonConvert.DeserializeObject< JSONOcrUploadVideoDTO>(job.OutputJson,new JsonSerializerSettings(){MissingMemberHandling = MissingMemberHandling.Ignore,ReferenceLoopHandling = ReferenceLoopHandling.Ignore,MetadataPropertyHandling = MetadataPropertyHandling.Ignore});
                            job.Status = "Started";
                            json1.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json1);
                            dbContext.Update(job);
                            var asset1 = OCR.Program.UploadOcrAsset(json1.input.FilePath);
                            json1.LastUpdated = DateTime.UtcNow;
                            json1.IAssetId = asset1.Id;
                            job.IsUploadedToCloud = true;
                            job.IsCloudJobFinished = true;
                            job.OutputJson = JsonConvert.SerializeObject(json1);
                            job.Status = "Finished";
                            dbContext.Update(job);
                            break;
                        case RegExJobType.OcrCreateMediaAnalyticsJob:
                            //
                            var json2 = JsonConvert.DeserializeObject<JSONOcrCreateJobDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                            job.Status = "Started";
                            json2.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json2);
                            dbContext.Update(job);
                            var configurationFile = json2.input.FilePath + ".ocrinput.json";
                            OCR.Program.CreateVideoInputJson(json2.input.FilePath, configurationFile);
                            var iasset = OCR.Program.GetAsset(json2.IAssetId);
                            var job2 = OCR.Program.CreateOcrJob(json2.input.FilePath,iasset);
                            job2.Submit();
                            json2.IJobId = job2.Id;
                            json2.ITaskId = job2.Tasks[0].Id;
                            json2.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json2);
                            job.Status = "Finished";
                            dbContext.Update(job);
                            break;
                        case RegExJobType.OcrDownloadResultJob:

                            //
                            var json3 = JsonConvert.DeserializeObject<JSONOcrDownloadResultsDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                            job.Status = "Started";
                            json3.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json3);
                            dbContext.Update(job);
                            var job3 = OCR.Program.GetJob(json3.IJobId);
                            Task progressJobTask = job3.GetExecutionProgressTask(CancellationToken.None);

                            progressJobTask.Wait();

                            // If job state is Error, the event handling
                            // method for job progress should log errors.  Here we check
                            // for error state and exit if needed.
                            if (job3.State == JobState.Error)
                            {
                                ErrorDetail error = job3.Tasks.First().ErrorDetails.First();
                                Console.WriteLine(string.Format("Error: {0}. {1}",
                                    error.Code,
                                    error.Message));
                                //return null;
                                job.Status = "ERROR";
                                job.IsCloudJobFinished = true;
                                break;
                            }

                            var outAsset=  job3.OutputMediaAssets[0];
                            Directory.CreateDirectory(json3.OutputFileName);
                            OCR.Program.DownloadAsset(outAsset,json3.OutputFileName);
                            json3.IAssetId = outAsset.Id;
                            json3.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json3);
                            job.IsCloudJobFinished = true;
                            job.Status = "Finished";
                            dbContext.Update(job);
                            break;
                        case RegExJobType.OcrCreateLocalJpegSnippetJob:
                            //
                            var json4 = JsonConvert.DeserializeObject<JSONOcrCreateSnippetDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                            job.Status = "Started";
                            json4.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json4);
                            dbContext.Update(job);
                            var   outAsset4 = OCR.Program.GetAsset(json4.IAssetId);
                            Directory.CreateDirectory(json4.OutputFileName);
                            var matches= ProcessJsonProgram.ExtractRegExMatches(outAsset4.AssetFiles.First().Name);
                            ProcessJsonProgram.ProcessOutputs(json4.OutputFileName.Replace(".output.json",""),needJpegs:true,needVideo:false,matches:matches, clipDuration: json4.clipDuration);
                            json4.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json4);
                            job.IsCloudJobFinished = true;
                            job.Status = "Finished";
                            dbContext.Update(job);
                            break;
                        case RegExJobType.OcrCreateLocalVideoSnippetJob:
                            //
                            var json5 = JsonConvert.DeserializeObject<JSONOcrCreateSnippetDTO>(job.OutputJson, new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore, ReferenceLoopHandling = ReferenceLoopHandling.Ignore, MetadataPropertyHandling = MetadataPropertyHandling.Ignore });
                            job.Status = "Started";
                            json5.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json5);
                            dbContext.Update(job);
                            var outAsset5 = OCR.Program.GetAsset(json5.IAssetId);
                            Directory.CreateDirectory(json5.OutputFileName);
                            var matches5 = ProcessJsonProgram.ExtractRegExMatches(outAsset5.AssetFiles.First().Name);
                            ProcessJsonProgram.ProcessOutputs(json5.OutputFileName.Replace(".output.json", ""), needJpegs: false, needVideo: true, matches: matches5, clipDuration:json5.clipDuration);
                            json5.LastUpdated = DateTime.UtcNow;
                            job.OutputJson = JsonConvert.SerializeObject(json5);
                            job.IsCloudJobFinished = true;
                            job.Status = "Finished";
                            dbContext.Update(job);
                            break;
                        default:
                            break;
                    }

                }

                // Run jobs at next minute.
                Thread.Sleep((60 - DateTime.Now.Second) * 1000);

            }

        }
    }
}
