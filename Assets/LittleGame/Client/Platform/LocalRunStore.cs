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
        public RunSave Load() => MVC.LocalSaveService.ReadFile<RunSave>(path);
        public void Save(RunSave data) => MVC.LocalSaveService.WriteFile(path, data);
    }
    public sealed class UnavailableCommerceService : ICommerceService
    {
        public bool Available => false;
        public void Purchase(string productId) => throw new InvalidOperationException("尚未接入 IAP，原型不提供购买");
    }
}
