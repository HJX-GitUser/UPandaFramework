using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

//[ExecuteInEditMode] //Disable if you don't care about previewing outside of play mode
public class WorldSpaceOverlayUI : MonoBehaviour
{
    private const string shaderTestMode = "unity_GUIZTestMode"; //The magic property we need to set
    [SerializeField] UnityEngine.Rendering.CompareFunction desiredUIComparison = UnityEngine.Rendering.CompareFunction.Always; //If you want to try out other effects
    [Tooltip("Set to blank to automatically populate from the child UI elements")]
    [SerializeField] Graphic[] uiGraphicsToApplyTo;
    [Tooltip("Set to blank to automatically populate from the child UI elements")]
    [SerializeField] TextMeshProUGUI[] uiTextsToApplyTo;
    //Allows us to reuse materials
    private Dictionary<Material, Material> materialMappings = new Dictionary<Material, Material>();
    protected virtual void Start()
    {
        if (uiGraphicsToApplyTo == null || uiGraphicsToApplyTo.Length == 0)
        {
            uiGraphicsToApplyTo = gameObject.GetComponentsInChildren<Graphic>();
        }
        if (uiTextsToApplyTo == null || uiTextsToApplyTo.Length == 0)
        {
            uiTextsToApplyTo = gameObject.GetComponentsInChildren<TextMeshProUGUI>();
        }
        foreach (var graphic in uiGraphicsToApplyTo)
        {
            if (graphic == null || graphic is TextMeshProUGUI) continue;
            Material material = graphic.material;
            if (material == null)
            {
                Debug.LogError($"{nameof(WorldSpaceOverlayUI)}: skipping target without material {graphic.name}.{graphic.GetType().Name}");
                continue;
            }
            if (!materialMappings.TryGetValue(material, out Material materialCopy))
            {
                materialCopy = new Material(material);
                materialMappings.Add(material, materialCopy);
            }
            materialCopy.SetInt(shaderTestMode, (int)desiredUIComparison);
            graphic.material = materialCopy;
        }
        foreach (var text in uiTextsToApplyTo)
        {
            if (text == null) continue;
            Material material = text.fontSharedMaterial;
            if (material == null)
            {
                Debug.LogError($"{nameof(WorldSpaceOverlayUI)}: skipping target without material {text.name}.{text.GetType().Name}");
                continue;
            }
            if (!materialMappings.TryGetValue(material, out Material materialCopy))
            {
                materialCopy = new Material(material);
                materialMappings.Add(material, materialCopy);
            }
            materialCopy.SetInt(shaderTestMode, (int)desiredUIComparison);
            text.fontSharedMaterial = materialCopy;
        }
    }
    protected virtual void OnDestroy()
    {
        // 组件被单独移除时，Graphic 仍存活，先恢复共享材质。
        foreach (var pair in materialMappings)
        {
            if (uiGraphicsToApplyTo != null)
                foreach (var graphic in uiGraphicsToApplyTo)
                    if (graphic != null && !(graphic is TextMeshProUGUI) && graphic.material == pair.Value)
                        graphic.material = pair.Key;
            if (uiTextsToApplyTo != null)
                foreach (var text in uiTextsToApplyTo)
                    if (text != null && text.fontSharedMaterial == pair.Value) text.fontSharedMaterial = pair.Key;
        }
        foreach (var material in materialMappings.Values)
            if (material != null) Destroy(material);
        materialMappings.Clear();
    }
}
