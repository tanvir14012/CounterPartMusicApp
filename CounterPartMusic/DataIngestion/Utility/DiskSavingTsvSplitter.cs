namespace CounterPartMusic.DataIngestion.Utility
{
    public static class DiskSavingTsvSplitter
    {
        public static void Split(string filePath)
        {
            SplitIteratively(filePath);
        }
        private static void SplitIteratively(string filePath)
        {
            var size = new FileInfo(filePath).Length;
            if (size <= ConfigurationOptions.ChunkSizeInBytes)
                return;

            var ChunkNo = (int)Math.Ceiling((double)size / ConfigurationOptions.ChunkSizeInBytes);
            var fileInfo = new FileInfo(filePath);
            var chunkFileRoot = fileInfo.Name.Replace(fileInfo.Extension, string.Empty);
            byte[] buffer = new byte[ConfigurationOptions.BufferSize];

            using (var fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite))
            {
                while (fs.Length > ConfigurationOptions.ChunkSizeInBytes)
                {
                    var startPos = fs.Length - ConfigurationOptions.ChunkSizeInBytes;
                    if (startPos <= 0)
                        break;

                    fs.Seek(startPos, SeekOrigin.Begin);
                    while (startPos < fs.Length && fs.ReadByte() != '\n') { startPos++; }
                    startPos++; //Do not read the newline

                    if (startPos >= fs.Length)
                    {
                        fs.SetLength(fs.Length - ConfigurationOptions.ChunkSizeInBytes);
                        continue;
                    }

                    var chunkFileName = $"{chunkFileRoot}_{ChunkNo--}" + fileInfo.Extension;
                    var chunkFilePath = Path.Combine(fileInfo.Directory.FullName, chunkFileName);

                    using (var writeStream = File.Open(chunkFilePath, FileMode.Create, FileAccess.Write))
                    {
                        fs.Seek(startPos, SeekOrigin.Begin);
                        long bytesToCopy = fs.Length - startPos;
                        long bytesCopied = 0;

                        while (bytesCopied < bytesToCopy)
                        {
                            int bytesRead = fs.Read(buffer, 0, buffer.Length);
                            if (bytesRead > 0)
                            {
                                writeStream.Write(buffer, 0, bytesRead);
                                bytesCopied += bytesRead;
                            }
                        }
                    }

                    fs.SetLength(startPos);
                }

            }

        }

    }
}
