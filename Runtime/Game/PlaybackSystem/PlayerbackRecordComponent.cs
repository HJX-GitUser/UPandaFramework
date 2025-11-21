using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerbackRecordComponent : MonoBehaviour
{
    private bool recordState = false;//记录状态
    public int instanceID;
    [HideInInspector]
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

    public void SetRecord(bool arg)
    {
        recordState = arg;
    }

    private void FixedUpdate()
    {
        if (recordState)
        {
            recordData.playbackRecordDatas.Add(new PlaybackRecordData(Time.time, transform.position, transform.rotation, transform.localScale));
        }
    }

    public void Replay(List<PlaybackRecordData> arg)
    {
        StartCoroutine(IReplay(arg));
    }

    private IEnumerator IReplay(List<PlaybackRecordData> arg)
    {
        Debug.Log($"回放数据count：{arg.Count}");
        int i = 0;
        while (i < arg.Count)
        {
            Debug.Log($"执行index {i}/{arg.Count - 1}");
            setState(arg[i]);
            i++;
            yield return null;
        }
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
    public List<PlaybackRecordData> playbackRecordDatas;
}
