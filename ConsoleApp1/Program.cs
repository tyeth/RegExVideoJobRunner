using System;
using System.Linq;
using System.Threading;

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
                            //
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
