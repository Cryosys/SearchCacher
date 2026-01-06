using Newtonsoft.Json;
using System.Collections.Concurrent;

namespace SearchCacher
{
    public class Statistics
    {
        [JsonIgnore]
        public string Source { get; }

        [JsonIgnore]
        public bool IsDirty { get; private set; } = false;

        [JsonProperty("CollectStats")]
        public bool CollectStats { get; set; } = false;

        public Statistics(string? statsPath, string source, bool collectStats)
        {
            _statsPath = statsPath;
            Source = source;
            CollectStats = collectStats;
        }

        public IEnumerable<(string path, long size)> GetTop(int start, int end)
        {
            using var scope = _statLock.EnterScope();

            // Validate min max
            start = Math.Max(0, Math.Min(start, _largestFiles.Count - 1));
            end = Math.Max(0, Math.Min(end, _largestFiles.Count));

            var range = _largestFiles.Take(new Range(start, end));
            return range.Select(stat => (stat.Path, stat.Size));
        }

        internal void AddFile(File file) => AddFile(file.Size, file.Hash, file.FullPath);

        public void AddFile(long size, long hash, string path)
        {
            if(!CollectStats)
                return;

            _fileQueue.Add(new QueueItem(QueueAction.Add, size, hash, path));
        }

        internal void RemoveFile(File file) => RemoveFile(file.Hash);

        public void RemoveFile(long hash)
        {
            if (!CollectStats)
                return;

            _fileQueue.Add(new QueueItem(QueueAction.Remove, 0, hash, ""));
        }

        internal void Load()
        {
            if (string.IsNullOrEmpty(_statsPath))
                throw new Exception("Stat path not valid");

            using var scope = _statLock.EnterScope();

            // Thats the same as if the object was newly created
            if (!System.IO.File.Exists(_statsPath))
                return;

            string statsJson = System.IO.File.ReadAllText(_statsPath);
            var stats = statsJson.FromCryJson<Statistics>();
            if (stats is null)
                throw new Exception($"Save statistics in {_statsPath} are invalid");

            _largestFiles = new(stats._largestFiles);
            _hashes = new(_largestFiles.Select(f => f.Hash));
            CollectStats = stats.CollectStats;
            IsDirty = false;
        }

        internal void Save()
        {
            if (string.IsNullOrEmpty(_statsPath))
                throw new Exception("Stat path not valid");

            using var scope = _statLock.EnterScope();

            if (!IsDirty)
                return;

            string stats = this.ToCryJson();
            System.IO.File.WriteAllText(_statsPath, stats);
            IsDirty = false;
        }

        public void Start()
        {
            using var startStopScope = _startStopLock.EnterScope();

            // Thread is already started
            if (_queueThread != null)
                return;

            _queueThread = new Thread(_QueueThread) { Name = $"Stat Queue Thread - {Source}" };
            _queueThread.Start();
            _startSignal.WaitOne(5000);
        }

        public void Stop()
        {
            using var startStopScope = _startStopLock.EnterScope();

            // Thread is already stopped
            if (_queueThread == null)
                return;

            _queueCancelTokenSource.Cancel();
            _queueThread.Join();
        }

        private void _QueueThread()
        {
            // Signal that the queue started
            _startSignal.Set();

            while (!_queueCancelTokenSource.IsCancellationRequested)
            {
                if (!_fileQueue.TryTake(out var item, 1000))
                    continue;

                using var scope = _statLock.EnterScope();
                switch (item.Action)
                {
                    case QueueAction.Add:
                        _AddFile(item.Size, item.Hash, item.Path);
                        break;
                    case QueueAction.Remove:
                        _RemoveFile(item.Hash);
                        break;
                }
            }
        }

        private void _AddFile(long size, long hash, string path)
        {
            _RemoveFile(hash);

            if (_largestFiles.Count == 0)
            {
                _largestFiles.Add(new StatFile(path, hash, size));
                _hashes.Add(hash);
                _smallesFileSize = size;
                IsDirty = true;
                return;
            }

            if (_largestFiles.Count >= MaxStats && size <= _smallesFileSize)
                return;

            for (int i = 0; i < _largestFiles.Count; i++)
            {
                if (size >= _largestFiles[i].Size)
                {
                    _largestFiles.Insert(i, new StatFile(path, hash, size));
                    _hashes.Add(hash);

                    if (_largestFiles.Count > MaxStats)
                        _largestFiles.RemoveAt(_largestFiles.Count - 1);

                    if (i == _largestFiles.Count - 1)
                        _smallesFileSize = size;

                    IsDirty = true;
                    break;
                }
            }
        }

        private void _RemoveFile(long hash)
        {
            if (!_hashes.Contains(hash))
                return;

            for (int i = 0; i < _largestFiles.Count; i++)
            {
                if (_largestFiles[i].Hash.Equals(hash))
                {
                    var curFile = _largestFiles[i];
                    _largestFiles.RemoveAt(i);
                    _hashes.Remove(hash);

                    if (_largestFiles.Count > 0)
                        _smallesFileSize = _largestFiles.Last().Size;

                    IsDirty = true;
                    break;
                }
            }

            if (_largestFiles.Count == 0)
                _smallesFileSize = 0;
        }


        [JsonProperty("LargestFiles")]
        private List<StatFile> _largestFiles = new();
        private long _smallesFileSize = long.MinValue;

        private Lock _statLock = new();
        private Lock _startStopLock = new();
        private Thread? _queueThread;
        private CancellationTokenSource _queueCancelTokenSource = new CancellationTokenSource();
        private AutoResetEvent _startSignal = new AutoResetEvent(false);

        private HashSet<long> _hashes = new();
        private string? _statsPath;

        private const int MaxStats = 25;
        private BlockingCollection<QueueItem> _fileQueue = new();

        private struct QueueItem
        {
            public QueueAction Action { get; }

            public long Size { get; }

            public long Hash { get; }

            public string Path { get; }

            public QueueItem(QueueAction action, long size, long hash, string path)
            {
                Action = action;
                Size = size;
                Hash = hash;
                Path = path;
            }
        }

        private enum QueueAction
        {
            Add,
            Remove
        }
    }

    internal class StatFile
    {
        public string Path { get; set; }

        public long Hash { get; set; }

        public long Size { get; set; }

        public StatFile(string path, long hash, long size)
        {
            Path = path;
            Hash = hash;
            Size = size;
        }
    }
}
