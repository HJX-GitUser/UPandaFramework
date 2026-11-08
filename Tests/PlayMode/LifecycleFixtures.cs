using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.TestTools;

namespace UPandaGF.Tests
{

[Serializable]
public class LifecycleFixtureConfig
{
    public string assetRoot, resourceKey, bundleDirectory;
    public static string FilePath => Path.Combine(Directory.GetCurrentDirectory(), "Temp", "upanda-lifecycle-fixtures.json");
}

/// <summary>PlayMode 前生成隔离夹具，结束后删除仅本轮创建的 Assets 目录。</summary>
public class LifecycleFixtures : IPrebuildSetup, IPostBuildCleanup
{
    public void Setup()
    {
#if UNITY_EDITOR
        string suffix = Guid.NewGuid().ToString("N");
        var config = new LifecycleFixtureConfig {
            assetRoot = "Assets/UPandaLifecycleTestFixtures_" + suffix,
            resourceKey = "UPandaLifecycle_" + suffix,
            bundleDirectory = Path.GetFullPath("Temp/LifecycleFixtures/" + suffix + "/AuditBundles")
        };
        Directory.CreateDirectory("Temp");
        File.WriteAllText(LifecycleFixtureConfig.FilePath, JsonUtility.ToJson(config));
        string resources = config.assetRoot + "/Resources/" + config.resourceKey;
        Directory.CreateDirectory(resources);
        using (var stream = File.Create(resources + "/Clip.wav"))
        using (var writer = new BinaryWriter(stream))
        {
            int bytes = 22050 * 2;
            writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + bytes); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(22050); writer.Write(44100); writer.Write((short)2); writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(bytes); writer.Write(new byte[bytes]);
        }
        UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        var audio = new GameObject("AudioPrefab");
        var source = audio.AddComponent<AudioSource>(); source.playOnAwake = false;
        source.clip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(resources + "/Clip.wav");
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(audio, resources + "/AudioPrefab.prefab"); UnityEngine.Object.DestroyImmediate(audio);
        var material = new Material(Shader.Find("Standard"));
        string materialPath = config.assetRoot + "/Shared.mat", prefabPath = config.assetRoot + "/Box.prefab";
        UnityEditor.AssetDatabase.CreateAsset(material, materialPath);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube); cube.GetComponent<Renderer>().sharedMaterial = material;
        UnityEditor.PrefabUtility.SaveAsPrefabAsset(cube, prefabPath); UnityEngine.Object.DestroyImmediate(cube);
        File.WriteAllText(config.assetRoot + "/External.txt", "External bundle owner");
        UnityEditor.AssetDatabase.Refresh(UnityEditor.ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory(config.bundleDirectory);
        var manifest = UnityEditor.BuildPipeline.BuildAssetBundles(config.bundleDirectory, new[] {
            new UnityEditor.AssetBundleBuild { assetBundleName = "audit_target", assetNames = new[] { prefabPath } },
            new UnityEditor.AssetBundleBuild { assetBundleName = "audit_shared", assetNames = new[] { materialPath } },
            new UnityEditor.AssetBundleBuild { assetBundleName = "audit_external", assetNames = new[] { config.assetRoot + "/External.txt" } }
        }, UnityEditor.BuildAssetBundleOptions.ChunkBasedCompression, UnityEditor.BuildTarget.StandaloneWindows64);
        if (manifest == null) throw new Exception("Lifecycle fixture AB build failed");
#endif
    }
    public void Cleanup()
    {
#if UNITY_EDITOR
        if (!File.Exists(LifecycleFixtureConfig.FilePath)) return;
        var config = JsonUtility.FromJson<LifecycleFixtureConfig>(File.ReadAllText(LifecycleFixtureConfig.FilePath));
        if (config.assetRoot.StartsWith("Assets/UPandaLifecycleTestFixtures_", StringComparison.Ordinal)
            && Path.GetFullPath(config.assetRoot).StartsWith(Path.GetFullPath("Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && config.assetRoot.IndexOf('/', 7) < 0 && config.assetRoot.IndexOf("..", StringComparison.Ordinal) < 0)
            UnityEditor.AssetDatabase.DeleteAsset(config.assetRoot);
        File.Delete(LifecycleFixtureConfig.FilePath);
#endif
    }
}

/// <summary>只监听 loopback，仅提供预先载入的三个测试包，受控延迟用于验证取消。</summary>
internal sealed class LifecycleBundleServer : IDisposable
{
    private readonly TcpListener listener;
    private readonly Dictionary<string, byte[]> files = new Dictionary<string, byte[]>();
    private volatile bool disposed;
    public string Url { get; private set; }
    public LifecycleBundleServer(string directory)
    {
        foreach (string name in new[] { "audit_target", "audit_shared", "audit_external" }) files[name] = File.ReadAllBytes(Path.Combine(directory, name));
        listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port;
        _ = Accept();
    }
    private async Task Accept()
    {
        while (!disposed)
        {
            try { var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false); _ = Serve(client); }
            catch (Exception) { if (disposed) return; throw; }
        }
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true))
                {
                    string first = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (first == null) return;
                    string line; do { line = await reader.ReadLineAsync().ConfigureAwait(false); } while (!string.IsNullOrEmpty(line));
                    string name = Path.GetFileName(first.Split(' ')[1]);
                    byte[] body;
                    if (!files.TryGetValue(name, out body)) return;
                    await Task.Delay(400).ConfigureAwait(false);
                    if (disposed) return;
                    byte[] header = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header, 0, header.Length).ConfigureAwait(false);
                    await stream.WriteAsync(body, 0, body.Length).ConfigureAwait(false);
                }
            }
            catch (IOException) { } catch (ObjectDisposedException) { } catch (SocketException) { }
        }
    }
    public void Dispose() { disposed = true; listener.Stop(); }
}

}