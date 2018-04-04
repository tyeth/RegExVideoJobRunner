namespace RegExVideoJobRunner
{


    public enum RegExJobType
    {
        OcrUploadVideoJob = 1,
        OcrCreateMediaAnalyticsJob = 2,
        OcrDownloadResultJob = 3,
        OcrCreateLocalJpegSnippetJob = 4,
        OcrCreateLocalVideoSnippetJob = 5,
    }


    public partial class Jobs
    {
        public string Id { get; set; }
        public bool IsCloudJobFinished { get; set; }
        public bool IsUploadedToCloud { get; set; }
        public string OutputJson { get; set; }
        public string Precursor { get; set; }
        public string UserId { get; set; }
        public string Status { get; set; }
        public RegExJobType JobType { get; set; }

    }
}
