using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UPandaGF;
using UnityEngine.UI;

[RequireComponent(typeof(CanvasGroup))]
[UILoadInfo(E_UI_Layer.Top, AssetLoadMethod.Resources, "UI/SimpleLoadUI")]
public class SimpleLoadUI : BasePanel
{
    public Text textL;
    public Slider progressSlider;
    private CanvasGroup canvasGroup;

    private Coroutine coroutine;

    protected override void OnAwake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
    }

    private void Start()
    {

    }

    public override void OnOpen(object panelArg)
    {
        PLogger.Log("SimpleLoadUI Open");
        progressSlider = GetControl<Slider>("ProgressSlider");
        textL = GetControl<Text>("message");
        textL.text = "";
        progressSlider.value = 0;
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }
    public override void OnClose()
    {
        PLogger.Log("SimpleLoadUI Close");
        SetSlieder(1, true);
    }


    public void SetMessage(float value, string message = "")
    {
        textL.text = message;
        SetSlieder(value);
    }

    private void SetSlieder(float value, bool closeTag = false)
    {
        value = Mathf.Clamp01(value);
        if (coroutine != null)
        {
            StopCoroutine(coroutine);
        }
        coroutine = StartCoroutine(ISetSlider(value, closeTag));
    }

    private IEnumerator ISetSlider(float targetValue, bool closeTag)
    {
        if (progressSlider.value > targetValue)
        {
            progressSlider.value = 0;
        }
        while (progressSlider.value < targetValue)
        {
            progressSlider.value += Time.deltaTime;
            yield return null;
        }
        if (closeTag)
        {
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

    }
}
