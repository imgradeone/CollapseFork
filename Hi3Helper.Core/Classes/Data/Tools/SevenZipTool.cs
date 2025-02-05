﻿using SevenZipExtractor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static Hi3Helper.Shared.Region.LauncherConfig;

namespace Hi3Helper.Data
{
    public class SevenZipTool : IDisposable
    {
        private string inputFilePath;
        private FileStream inputFileStream;
        private ArchiveFile archiveReader7Zip;
        private Stopwatch stopWatch;
        private int extractedCount;
        private IList<Entry> archiveEntries7Zip;
        private int entriesCount,
                    filesCount,
                    foldersCount;
        private long totalUncompressedSize,
                     totalCompressedSize;

        public void LoadArchive(string InputFile)
        {
            inputFilePath = InputFile;
            inputFileStream = new FileStream(InputFile, FileMode.Open, FileAccess.Read);
            archiveReader7Zip = new ArchiveFile(inputFileStream, null, @"Lib\7z.dll");
            archiveEntries7Zip = archiveReader7Zip.Entries;
            entriesCount = archiveEntries7Zip.Count();
            filesCount = archiveEntries7Zip.Sum(x => x.IsFolder ? 0 : 1);
            foldersCount = entriesCount - filesCount;

            totalUncompressedSize = archiveEntries7Zip.Sum(f => (long)f.Size);
            totalCompressedSize = archiveEntries7Zip.Sum(f => (long)f.PackedSize);
        }

        public long GetUncompressedSize(string inputFile)
        {
            inputFilePath = inputFile;
            inputFileStream = new FileStream(inputFile, FileMode.Open, FileAccess.Read);

            switch (Path.GetExtension(inputFile).ToLower())
            {
                case ".7z":
                case ".zip":
                    LoadArchive(inputFile);
                    return totalUncompressedSize;
                default:
                    throw new FormatException($"Format {Path.GetExtension(inputFile)} is unsupported!");
            }
        }

        public void Dispose()
        {
            inputFileStream.Dispose();
        }

        public void ExtractToDirectory(string outputDirectory, int thread = 1, CancellationToken token = new CancellationToken())
        {
            stopWatch = Stopwatch.StartNew();

            try
            {
                extractedCount = 0;

                Console.Write(string.Format(@"
Input File: {0}
Output Directory: {1}
Processing Thread: {2}
Files Count: {3}
Folders Count: {4}
Compressed Size: {5}
Uncompressed Size: {6}
Comp. > Uncomp. Ratio: {7}

Initializing...
",
                       inputFilePath,
                       outputDirectory,
                       $"{AppCurrentThread}",
                       filesCount,
                       foldersCount,
                       totalCompressedSize,
                       totalUncompressedSize,
                       $"{(float)((float)totalCompressedSize / totalUncompressedSize) * 100}%"));

                StartArchiveExtractThread(outputDirectory, token);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine($"Extraction cancelled!");
                throw new OperationCanceledException($"Extraction cancelled!");
            }

            stopWatch.Stop();
            // Get the elapsed time as a TimeSpan value.
            TimeSpan ts = stopWatch.Elapsed;

            // Format and display the TimeSpan value.
            string elapsedTime = string.Format("{0:00}:{1:00}:{2:00}.{3:00}",
                ts.Hours, ts.Minutes, ts.Seconds,
                ts.Milliseconds / 10);
            Console.WriteLine(elapsedTime, "RunTime");
            Console.WriteLine($"Extracted: {extractedCount} | Listed Entry: {filesCount}");
        }

        private void StartArchiveExtractThread(string OutputDirectory, CancellationToken Token)
        {
            stopWatch = Stopwatch.StartNew();
            string _outPath;
            archiveReader7Zip.ExtractProgress += ArchiveReaderAuto7ZipAdapter;
            using (archiveReader7Zip)
            {
                archiveReader7Zip.Extract(entry =>
                {
                    _outPath = Path.Combine(OutputDirectory, entry.FileName);
                    return _outPath;
                }, Token);
            }
            archiveReader7Zip.ExtractProgress -= ArchiveReaderAuto7ZipAdapter;
        }

        private void ArchiveReaderAuto7ZipAdapter(object sender, ArchiveFile.ExtractProgressProp e)
        {
            OnProgressChanged(new ExtractProgress(extractedCount, filesCount, (long)e.TotalRead, (long)e.TotalSize, stopWatch.Elapsed.TotalSeconds));
        }

        public event EventHandler<ExtractProgress> ExtractProgressChanged;
        protected virtual void OnProgressChanged(ExtractProgress e) => ExtractProgressChanged?.Invoke(this, e);
    }

    public class ExtractProgress : EventArgs
    {
        public ExtractProgress(int _totalExtractedCount, int _totalCount, long totalExtracted, long totalUncompressed, double totalSecond)
        {
            totalExtractedCount = _totalExtractedCount;
            totalCount = _totalCount;
            totalExtractedSize = totalExtracted;
            totalUncompressedSize = totalUncompressed;
            CurrentSpeed = (long)(totalExtracted / totalSecond);
        }
        public long totalExtractedCount { get; private set; }
        public long totalCount { get; private set; }
        public long totalExtractedSize { get; private set; }
        public long totalUncompressedSize { get; private set; }
        public float ProgressPercentage => ((float)totalExtractedSize / (float)totalUncompressedSize) * 100;
        public long CurrentSpeed { get; private set; }
        public TimeSpan TimeLeft => TimeSpan.FromSeconds((totalUncompressedSize - totalExtractedSize) / CurrentSpeed);
    }
}
