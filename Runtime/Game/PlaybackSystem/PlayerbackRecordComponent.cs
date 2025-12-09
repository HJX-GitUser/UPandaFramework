using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerbackRecordComponent : MonoBehaviour
{
    public int instanceID;
    public PlayerbackRecordComponentArg recordData;

    /// <summary>
    /// 回放索引
    /// </summary>
    private int replayIndex = 0;

    private void Reset()
    {
        RefreshID();
    }

    public void RefreshID()
    {
        instanceID = UniqueIDGenerator.GenerateID();
    }

    void Start()
    {
        recordData = new PlayerbackRecordComponentArg();
        recordData.instanceID = instanceID;
        PlaybackSystem.Instance.RegisterRecordComponent(this);
    }
    /// <summary>
    /// 重置记录
    /// </summary>
    public void ResetData()
    {
        recordData.playbackRecordDatas = new List<PlaybackRecordData>();
    }
    /// <summary>
    /// 新增数据
    /// </summary>
    /// <param name="timestamp"></param>
    public void AddData(float timestamp)
    {
        recordData.AddData(new PlaybackRecordData(timestamp, transform.position, transform.rotation, transform.localScale));
    }
    /// <summary>
    /// 回放数据设置
    /// </summary>
    /// <param name="arg"></param>
    public void SetReplayData(List<PlaybackRecordData> arg)
    {
        replayIndex = 0;
        recordData.playbackRecordDatas = arg;
        StartCoroutine(IReplay(arg));
    }

    public void SetReplay(float timestamp)
    {
        if (recordData.playbackRecordDatas == null || recordData.playbackRecordDatas.Count == 0) return;
        
    }

    private void GetRecordDatasIndex(float timestamp)
    {
        int recordIndex = 0;
        if (timestamp > recordData.playbackRecordDatas[replayIndex].timeStamp)
        {
        }
        else
        {

        }
    }

    private IEnumerator IReplay(List<PlaybackRecordData> arg)
    {
        Debug.Log($"回放数据count：{arg.Count}");
        setState(arg[0]);
        int i = 1;
        float timeRecord = 0;
        float totalTime = arg[arg.Count - 1].timeStamp;
        while (timeRecord < totalTime)
        {
            timeRecord += Time.deltaTime;
            if (i == arg.Count) break;
            while (i < arg.Count && arg[i].timeStamp < timeRecord)
            {
                Debug.LogWarning($"执行index {i}/{arg.Count - 1}");
                setState(arg[i]);
                i++;
            }

            yield return new WaitForSeconds(Time.deltaTime);
        }
        Debug.Log($"{transform.name}:{instanceID} 回放数据执行结束");
    }
    private void setState(PlaybackRecordData arg)
    {
        transform.position = arg.Position;
        transform.rotation = arg.Rotation;
        transform.localScale = arg.Scale;
    }
}

[System.Serializable]
public class PlayerbackRecordComponentArg
{
    public int instanceID;
    public List<PlaybackRecordData> playbackRecordDatas = new List<PlaybackRecordData>();
    public PlaybackRecordData currentData;

    public void AddData(PlaybackRecordData data)
    {
        if (playbackRecordDatas.Count == 0)
        {
            currentData = data;
            playbackRecordDatas.Add(data);
        }
        if (!currentData.IsEquals(data))
        {
            currentData = data;
            playbackRecordDatas.Add(data);
        }
    }
}
