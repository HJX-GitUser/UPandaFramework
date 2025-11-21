using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerbackRecordComponent : MonoBehaviour
{
    private bool recordState = false;//记录状态
    public int instanceID;
    public PlayerbackRecordComponentArg recordData;

    private void Reset()
    {
        instanceID = transform.GetInstanceID();
    }

    void Start()
    {
        recordData = new PlayerbackRecordComponentArg();
        recordData.instanceID = instanceID;
        recordData.playbackRecordDatas = new List<PlaybackRecordData>();
        PlaybackSystem.Instance.RegisterRecordComponent(this);
    }

    private float startTime;
    public void SetRecord(bool arg)
    {
        recordState = arg;
        if (arg) startTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (recordState)
        {
            recordData.AddData(new PlaybackRecordData(Time.time - startTime, transform.position, transform.rotation, transform.localScale));
        }
    }

    public void Replay(List<PlaybackRecordData> arg)
    {
        StartCoroutine(IReplay(arg));
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
