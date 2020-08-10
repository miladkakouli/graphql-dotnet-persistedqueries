using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;

namespace GraphQL.PersistedQueries
{
    public class CacheQueryMonitor
    {
        private readonly IDistributedCache _cacheProvider;
        private readonly string _fileAddress;

        public CacheQueryMonitor(IDistributedCache cacheProvider, string fileAddress)
        {
            _cacheProvider = cacheProvider;
            _fileAddress = fileAddress;
            RegisterCacheQueryWatcherAndFillCache();
        }

        public void RegisterCacheQueryWatcher()
        {
            var _fileSystemWatcher = new FileSystemWatcher();
            _fileSystemWatcher.Path = Environment.CurrentDirectory;
            _fileSystemWatcher.NotifyFilter = NotifyFilters.LastWrite;

            _fileSystemWatcher.Filter = _fileAddress;
            _fileSystemWatcher.Changed += OnChanged;
            _fileSystemWatcher.EnableRaisingEvents = true;
        }

        public void RegisterCacheQueryWatcherAndFillCache()
        {
            RegisterCacheQueryWatcher();
            FillCache(_fileAddress);
        }

        private void OnChanged(object source, FileSystemEventArgs e) => FillCache(e.FullPath);

        private void FillCache(string fullPath)
        {
            Thread.Sleep(TimeSpan.FromSeconds(2));
            var text = System.IO.File.ReadAllText(fullPath);
            var arrayDic = JsonConvert.DeserializeObject<IDictionary<string, string>>(text);
            foreach (var item in arrayDic)
            {
                _cacheProvider.Set(item.Key, Encoding.UTF8.GetBytes(item.Value));
            }
        }

    }
}
