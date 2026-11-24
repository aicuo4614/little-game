using System;
using System.IO;
using Newtonsoft.Json;
using LittleGame.Combat;

namespace LittleGame.Client
{
    public sealed class LocalRunStore : IRunStore
    {
        private readonly string path;
        public LocalRunStore(string path) { this.path = path; }
        public RunSave Load() => File.Exists(path) ? JsonConvert.DeserializeObject<RunSave>(File.ReadAllText(path)) ?? throw new InvalidDataException("空存档") : null;
        public void Save(RunSave data)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string staging = path + ".tmp";
            File.WriteAllText(staging, JsonConvert.SerializeObject(data, Formatting.Indented));
            // 同卷替换并保留上一次有效档；失败不删除原档。
            if (File.Exists(path)) File.Replace(staging, path, path + ".bak");
            else File.Move(staging, path);
        }
    }
    public sealed class UnavailableCommerceService : ICommerceService
    {
        public bool Available => false;
        public void Purchase(string productId) => throw new InvalidOperationException("尚未接入 IAP，原型不提供购买");
    }
}
