using System;

namespace RegExVideoJobRunner
{
    public partial class Files
    {
        public string Id { get; set; }
        public string FileNameFromUpload { get; set; }
        public string FilePath { get; set; }
        public string UserId { get; set; }
        public DateTime UploadTimeStamp { get; set; }
    }
}
