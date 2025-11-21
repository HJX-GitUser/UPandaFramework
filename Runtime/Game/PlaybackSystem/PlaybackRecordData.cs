using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class PlaybackRecordData
{
    public float timeStamp;
    public float[] position;
    public float[] rotation;
    public float[] scale;

    public PlaybackRecordData(float timeStamp, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        this.timeStamp = timeStamp;

        this.position = new float[3];
        this.position[0] = position.x;
        this.position[1] = position.y;
        this.position[2] = position.z;

        this.rotation = new float[4];
        this.rotation[0] = rotation.x;
        this.rotation[1] = rotation.y;
        this.rotation[2] = rotation.z;
        this.rotation[3] = rotation.w;

        this.scale = new float[3];
        this.scale[0] = scale.x;
        this.scale[1] = scale.y;
        this.scale[2] = scale.z;
    }

    public Vector3 Position => new Vector3(position[0], position[1], position[2]);
    public Quaternion Rotation => new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
    public Vector3 Scale => new Vector3(scale[0], scale[1], scale[2]);

    public bool IsEquals(PlaybackRecordData obj)
    {
        if (!Position.Equals(obj.Position)) return false;
        if (!Rotation.Equals(obj.Rotation)) return false;
        if (!Scale.Equals(obj.Scale)) return false;
        return true;
    }
}
