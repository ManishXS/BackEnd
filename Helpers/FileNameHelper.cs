namespace BackEnd.Helpers
{
    public static class FileNameHelper
    {
        private static int _fileCounter = 0;
        private static readonly object _lock = new object();

        public static string GenerateUniqueFileName(string originalFileName)
        {
            lock (_lock)
            {
                _fileCounter++;
                var fileExtension = Path.GetExtension(originalFileName);
                var baseName = Path.GetFileNameWithoutExtension(originalFileName);

                // Append counter to filename to ensure uniqueness
                return $"{baseName}-{_fileCounter}{fileExtension}";
            }
        }
    }
}
