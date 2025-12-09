using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading.Tasks;
using UnityEngine;
using UPandaGF;


public class PlaybackSystem : EagerMonoSingletonBase<PlaybackSystem>
{
    private static string SAVE_PATH;
    private static string FILE_EXTENSION = ".pb";// 存储文件后缀
    public Dictionary<int, PlayerbackRecordComponent> playerbackRecords = new Dictionary<int, PlayerbackRecordComponent>();

    private float timeRecord;

    private bool isRecording = false;

    protected override void OnAwake()
    {
        SAVE_PATH = Application.persistentDataPath + "/PlaybackSystemData/";// 数据类存储的位置
    }

    public void RegisterRecordComponent(PlayerbackRecordComponent arg)
    {
        if (playerbackRecords.ContainsKey(arg.instanceID))
        {
            Debug.LogError($"{arg.instanceID}存在相同id,{arg.transform.name}/{playerbackRecords[arg.instanceID].name}");
        }
        playerbackRecords[arg.instanceID] = arg;
    }

    public void SaveData()
    {
        StartCoroutine(IE_Save());
    }

    private IEnumerator IE_Save()
    {
        PlaybackSaveData data = new PlaybackSaveData();
        data.total = timeRecord;
        foreach (var item in playerbackRecords.Values)
        {
            data.datas.Add(item.recordData);
        }
        Debug.Log($"保存{data.datas.Count}个对象的数据");
        System.Threading.Tasks.Task task = System.Threading.Tasks.Task.Run(() =>
        {
            Save(data, "RecordData");
        });
        while (!task.IsCompleted)
        {
            Debug.Log("保存中...");
            yield return null;
        }
        Debug.Log($"保存完成");
    }

    /// <summary>
    /// 存储类对象数据
    /// </summary>
    /// <param name="obj">数据类</param>
    /// <param name="fileName">存储文件名称</param>
    private void Save(object obj, string fileName)
    {
        //先判断路径文件夹有没有
        if (!Directory.Exists(SAVE_PATH))
        {
            Directory.CreateDirectory(SAVE_PATH);
        }

        string path = SAVE_PATH + fileName + FILE_EXTENSION;
        Debug.Log("data save path:" + path);

        //改用内存流的方式可以对数据再做些操作，比如进行加密
        using (MemoryStream ms = new MemoryStream())
        {
            BinaryFormatter bf = new BinaryFormatter();
            bf.Serialize(ms, obj);

            byte[] bytes = CompressData(ms.GetBuffer());
            //ToDo:..在这里可以做一些加密的工作
            File.WriteAllBytes(path, bytes);
            ms.Close();
        }

    }
    /// <summary>
    /// 读取2进制数据，并转换成对象
    /// </summary>
    /// <typeparam name="T">对象类型</typeparam>
    /// <param name="fileName">存储文件名称</param>
    /// <returns>数据对象</returns>
    private T Load<T>(string fileName) where T : class
    {
        //如果不存在这个文件 就直接返回泛型对象的默认值
        if (!File.Exists(SAVE_PATH + fileName + FILE_EXTENSION))
        {
            return default(T);
        }
        T obj = null;

        byte[] bytes = DecompressData(File.ReadAllBytes(SAVE_PATH + fileName + FILE_EXTENSION));

        //ToDo..这里可以根据规则做解密的工作
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            BinaryFormatter bf = new BinaryFormatter();
            obj = bf.Deserialize(ms) as T;
            ms.Close();
        }
        return obj;
    }

    /// <summary>
    /// 压缩
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private byte[] CompressData(byte[] data)
    {
        using (MemoryStream ms = new MemoryStream())
        {
            using (GZipStream gz = new GZipStream(ms, System.IO.Compression.CompressionLevel.Optimal))
            {
                gz.Write(data, 0, data.Length);
            }
            return ms.ToArray();
        }
    }

    /// <summary>
    /// 解压
    /// </summary>
    /// <param name="compressedData"></param>
    /// <returns></returns>
    private byte[] DecompressData(byte[] compressedData)
    {
        using (MemoryStream ms = new MemoryStream(compressedData))
        using (GZipStream gz = new GZipStream(ms, System.IO.Compression.CompressionMode.Decompress))
        using (MemoryStream resultStream = new MemoryStream())
        {
            gz.CopyTo(resultStream);
            return resultStream.ToArray();
        }
    }


    public void EnableRecord()
    {
        Debug.Log("start record");
        timeRecord = Time.time;
        isRecording = true;
    }

    public void DisableeRecord()
    {
        timeRecord = Time.time - timeRecord;
        Debug.Log($"stop record,reocrd time:{timeRecord}");
        isRecording = false;
    }
    float _rt = 0;
    private void FixedUpdate()
    {
        if (isRecording)
        {
            _rt = Time.time;
            foreach (var item in playerbackRecords.Values)
            {
                item.AddData(Time.time - timeRecord);
            }
        }
    }

    public void Replay()
    {
        StartCoroutine(IE_Replay());
    }

    private IEnumerator IE_Replay()
    {
        PlaybackSaveData data = null;
        PLoger.Log("开始加载数据");
        System.Threading.Tasks.Task task = System.Threading.Tasks.Task.Run(() =>
        {
            data = Load<PlaybackSaveData>("RecordData");
        });
        while (!task.IsCompleted)
        {
            Debug.Log("加载中...");
            yield return null;
        }
        Debug.Log("加载结束：" + data.datas.Count);
        foreach (var item in data.datas)
        {
            if(!playerbackRecords.ContainsKey(item.instanceID))
            {
                Debug.LogError($"{item.instanceID} is null");
                continue;
            }
            playerbackRecords[item.instanceID].SetReplayData(item.playbackRecordDatas);
        }
    }

}

[System.Serializable]
public class PlaybackSaveData
{
    public float total = 0;
    public List<PlayerbackRecordComponentArg> datas = new List<PlayerbackRecordComponentArg>();
}

