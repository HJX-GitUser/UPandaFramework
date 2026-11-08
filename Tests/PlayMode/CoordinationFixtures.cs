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

public class CoordinationFixtures:IPrebuildSetup,IPostBuildCleanup
{
    private const string Config="Temp/upanda-coordination-scenes.json";
    [Serializable] public class Settings { public string folder; public string[] previous;public bool[] enabled; }
    public static string ScenePath => JsonUtility.FromJson<Settings>(File.ReadAllText(Config)).folder+"/Coordination.unity";
    public void Setup()
    {
#if UNITY_EDITOR
        var old=UnityEditor.EditorBuildSettings.scenes;
        var settings=new Settings{folder="Assets/UPandaCoordinationFixtures_"+Guid.NewGuid().ToString("N"),previous=new string[old.Length],enabled=new bool[old.Length]};
        for(int i=0;i<old.Length;i++){settings.previous[i]=old[i].path;settings.enabled[i]=old[i].enabled;}
        Directory.CreateDirectory("Temp");File.WriteAllText(Config,JsonUtility.ToJson(settings));
        Directory.CreateDirectory(settings.folder);UnityEditor.AssetDatabase.Refresh();
        var active=UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var scene=UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,UnityEditor.SceneManagement.NewSceneMode.Additive);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene,ScenePath);
        UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene,true);
        if(active.IsValid())UnityEngine.SceneManagement.SceneManager.SetActiveScene(active);
        var scenes=new List<UnityEditor.EditorBuildSettingsScene>(old){new UnityEditor.EditorBuildSettingsScene(ScenePath,true)};
        UnityEditor.EditorBuildSettings.scenes=scenes.ToArray();
#endif
    }
    public void Cleanup()
    {
#if UNITY_EDITOR
        if(!File.Exists(Config))return;
        var settings=JsonUtility.FromJson<Settings>(File.ReadAllText(Config));
        var old=new UnityEditor.EditorBuildSettingsScene[settings.previous.Length];
        for(int i=0;i<old.Length;i++)old[i]=new UnityEditor.EditorBuildSettingsScene(settings.previous[i],settings.enabled[i]);
        UnityEditor.EditorBuildSettings.scenes=old;
        if(settings.folder.StartsWith("Assets/UPandaCoordinationFixtures_",StringComparison.Ordinal)&&settings.folder.IndexOf('/',7)<0&&settings.folder.IndexOf("..",StringComparison.Ordinal)<0
           &&Path.GetFullPath(settings.folder).StartsWith(Path.GetFullPath("Assets")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))UnityEditor.AssetDatabase.DeleteAsset(settings.folder);
        File.Delete(Config);
#endif
    }
}

/// <summary>Only loopback HTTP, no network service or administrator URL reservation required.</summary>
internal sealed class CoordinationHttpServer:IDisposable
{
    private readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
    private readonly HashSet<TcpClient> clients=new HashSet<TcpClient>();
    private readonly object gate=new object();private bool disposed;
    public string Url{get;}
    public CoordinationHttpServer(){listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port;_=Accept();}
    private async Task Accept()
    {
        while(true)
        {
            try{var client=await listener.AcceptTcpClientAsync().ConfigureAwait(false);lock(gate){if(disposed){client.Dispose();return;}clients.Add(client);}_=Serve(client);}
            catch(ObjectDisposedException){return;}catch(SocketException){return;}
        }
    }
    private async Task Serve(TcpClient client)
    {
        using(client)
        {
            try
            {
                var stream=client.GetStream();
                using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true))
                {
                    string first=await reader.ReadLineAsync().ConfigureAwait(false);if(first==null)return;
                    string line;do{line=await reader.ReadLineAsync().ConfigureAwait(false);}while(!string.IsNullOrEmpty(line));
                    string path=first.Split(' ')[1];if(path.Contains("slow"))await Task.Delay(500).ConfigureAwait(false);
                    byte[] body=Encoding.UTF8.GetBytes("{\"value\":1}");
                    byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+(path.Contains("fail")?"503 Unavailable":"200 OK")+"\r\nContent-Length: "+body.Length+"\r\nConnection: close\r\n\r\n");
                    await stream.WriteAsync(header,0,header.Length).ConfigureAwait(false);await stream.WriteAsync(body,0,body.Length).ConfigureAwait(false);
                }
            }
            catch(IOException){}catch(ObjectDisposedException){}catch(SocketException){}
            finally{lock(gate)clients.Remove(client);}
        }
    }
    public void Dispose(){lock(gate){disposed=true;listener.Stop();foreach(var client in clients)client.Dispose();clients.Clear();}}
}

}