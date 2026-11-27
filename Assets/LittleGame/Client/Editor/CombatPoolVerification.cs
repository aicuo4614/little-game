using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace LittleGame.Client.Editor
{
    public static class CombatPoolVerification
    {
        [MenuItem("Little Game/Combat/Verify Pool Prewarm")]
        public static void Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("请先进入战斗 Play。");
            var root = new GameObject("PoolVerification");
            var entries = new List<ResourceEntrySnapshot>();
            MVC.ResourceService.CopyEntriesSnapshot(entries);
            var before = entries.ToDictionary(e => e.Key, e => e.RefCount);
            var checks = new List<string>();
            var pool = PoolMgr.Instance.CreateGroup(root.transform);
            try
            {
                foreach (var item in new[] { ("Unit/Low/Grunt/Grunt", ResourcePackageName.UnitPackage), ("Effect/Low/fx_arrow_basic", ResourcePackageName.EffectPackage) })
                {
                    int created = pool.CreatedCount;
                    pool.Prewarm(item.Item1, item.Item2);
                    pool.Prewarm(item.Item1, item.Item2);
                    Require(pool.CreatedCount == created + 1, "重复预热只创建一只：" + item.Item1, checks);
                    var first = pool.Spawn(item.Item1, root.transform, item.Item2, false);
                    int instanceId = first.GetInstanceID();
                    Require(!first.activeInHierarchy, "可在激活前准备实例：" + item.Item1, checks);
                    first.transform.localScale = Vector3.one * 5;
                    pool.Despawn(first);
                    pool.Despawn(first);
                    var second = pool.Spawn(item.Item1, root.transform, item.Item2, false);
                    Require(second.GetInstanceID() == instanceId && pool.CreatedCount == created + 1, "归池后复用原实例：" + item.Item1, checks);
                    Require(second.transform.localScale != Vector3.one * 5, "复用时恢复预制体缩放：" + item.Item1, checks);
                    foreach (var particle in second.GetComponentsInChildren<ParticleSystem>(true))
                        Require(particle.particleCount == 0, "复用粒子已清空：" + particle.name, checks);
                    pool.Despawn(second);
                }
                Require(pool.ActiveCount == 0 && pool.IdleCount == 2 && pool.ReusedCount == 4, "借出归还计数正确", checks);
            }
            finally
            {
                pool.Dispose();
                pool.Dispose();
                UnityEngine.Object.Destroy(root);
            }
            entries.Clear();
            MVC.ResourceService.CopyEntriesSnapshot(entries);
            var after = entries.ToDictionary(e => e.Key, e => e.RefCount);
            Require(before.Count == after.Count && before.All(e => after.TryGetValue(e.Key, out int refs) && refs == e.Value), "池销毁后资源引用计数恢复", checks);
            Directory.CreateDirectory("Reports/Performance/20261127");
            File.WriteAllText("Reports/Performance/20261127/pool-validation.json", JsonConvert.SerializeObject(new { platform = Application.platform.ToString(), checks }, Formatting.Indented));
            Debug.Log("对象池预热验证通过：" + checks.Count + " 项。");
        }

        private static void Require(bool condition, string message, List<string> checks)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks.Add(message);
        }
    }
}
