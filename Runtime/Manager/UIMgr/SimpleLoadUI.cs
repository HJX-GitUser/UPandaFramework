using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;
using UnityEngine.UI;

public class SimpleLoadUI : BasePanel
{
    public Text textL;
    public Slider progressSlider;

    public override void OnOpen()
    {
        PLogger.Log("SimpleLoadUI Open");
        progressSlider = GetControl<Slider>("ProgressSlider");
        textL = GetControl<Text>("message");
        textL.text = "";
        progressSlider.value = 0;
    }
    public override void OnClose()
    {
        PLogger.Log("SimpleLoadUI Close");
    }


    public void SetMessage(float value, string message = null)
    {
        progressSlider.value = value;
        if (message != null) textL.text = message;
    }
}
