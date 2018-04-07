using System;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Linq.JsonPath;

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
                            if (precusor.Status != "Finished" || !precusor.Status.StartsWith("Purg") || precusor.Status != "Done") continu=true;
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

                            var json = JsonConvert.DeserializeObject< JSONOcrUploadVideoDTO>(job.OutputJson,new JsonSerializerSettings(){MissingMemberHandling = MissingMemberHandling.Ignore,ReferenceLoopHandling = ReferenceLoopHandling.Ignore,MetadataPropertyHandling = MetadataPropertyHandling.Ignore});
                            job.Status = "Started";
                            OCR.Program.Main(new string[]{json.input.FilePath});
                            job.Status = "Finished";
                            break;
                        case RegExJobType.OcrCreateMediaAnalyticsJob:
                            //

                            break;
                        case RegExJobType.OcrDownloadResultJob:
                            //
                            break;
                        case RegExJobType.OcrCreateLocalJpegSnippetJob:
                            //
                            break;
                        case RegExJobType.OcrCreateLocalVideoSnippetJob:
                            //
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
