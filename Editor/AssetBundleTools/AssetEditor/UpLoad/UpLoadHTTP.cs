using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class HTTPUpLoadABConfig
{
    public string serverUrl = "http://127.0.0.1/upload";
    public string selectedFilePath = "";
    public string uploadStatus = "";
    public bool isUploading = false;
    public float uploadProgress = 0f;
}

public class UpLoadHTTP 
{
    public HTTPUpLoadABConfig HTTPConfig;

}
