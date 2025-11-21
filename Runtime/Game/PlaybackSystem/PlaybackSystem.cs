using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;

public class PlaybackSystem : BaseMonoSingleton<PlaybackSystem>
{
    private static string SAVE_PATH;
    private static string FILE_EXTENSION = ".pb";// 存储文件后缀
    public Dictionary<int, PlayerbackRecordComponent> playerbackRecords = new Dictionary<int, PlayerbackRecordComponent>();

    protected override void Initialize()
    {
        SAVE_PATH = Application.persistentDataPath + "/Data/";// 数据类存储的位置
    }

    public void RegisterRecordComponent(PlayerbackRecordComponent arg)
    {
        playerbackRecords[arg.instanceID] = arg;
    }

    public void SaveData()
    {
        PlaybackSaveData data = new PlaybackSaveData();
        foreach (var item in playerbackRecords.Values)
        {
            data.datas.Add(item.recordData);
        }
        Save(data, "RecordData");
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

            byte[] bytes = ms.GetBuffer();
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

        byte[] bytes = File.ReadAllBytes(SAVE_PATH + fileName + FILE_EXTENSION);
        //ToDo..这里可以根据规则做解密的工作
        using (MemoryStream ms = new MemoryStream(bytes))
        {
            BinaryFormatter bf = new BinaryFormatter();
            obj = bf.Deserialize(ms) as T;
            ms.Close();
        }
        return obj;
    }


    public void EnableRecord()
    {
        Debug.Log("start record");
        foreach (var item in playerbackRecords.Values)
        {
            item.SetRecord(true);
        }
    }

    public void DisableeRecord()
    {
        Debug.Log("stop record");
        foreach (var item in playerbackRecords.Values)
        {
            item.SetRecord(false);
        }
    }

    public void Replay()
    {
        PlaybackSaveData data = Load<PlaybackSaveData>("RecordData");
        Debug.Log(data.datas.Count);
        foreach (var item in data.datas)
        {
            playerbackRecords[item.instanceID].Replay(item.playbackRecordDatas);
        }
    }

}

[System.Serializable]
public class PlaybackSaveData
{
    public List<PlayerbackRecordComponentArg> datas = new List<PlayerbackRecordComponentArg>();
}

