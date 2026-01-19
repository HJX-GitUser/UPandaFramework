using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;
using TMPro;
using UnityEngine.UI;

public class SimpleLoadUI : BasePanel
{
    public TMP_Text textMeshPro;
    public Slider progressSlider;

    public override void OnOpen()
    {
        PLogger.Log("SimpleLoadUI Open");
        progressSlider = GetControl<Slider>("ProgressSlider");
        textMeshPro = GetControl<TMP_Text>("message");
        textMeshPro.text = "";
        progressSlider.value = 0;
    }
    public override void OnClose()
    {
        PLogger.Log("SimpleLoadUI Close");
    }


    public void SetMessage(float value, string message = null)
    {
        progressSlider.value = value;
        if (message != null) textMeshPro.text = message;
    }
}
