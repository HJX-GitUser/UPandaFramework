using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 面板基类
/// 通过代码快速的找到所有的子控件，节约找控件的工作量
/// 在子类中处理逻辑 
/// </summary>
public class BasePanel : MonoBehaviour
{
    /// <summary>
    /// UI组件容器
    /// UIBehaviour是所有UI组件的基类，UI组件都是直接或者间接继承UIBehaviour这个抽象类的，
    /// 它继承自MonoBehavior，所以拥有和Unity相同的生命周期
    /// </summary>
    private Dictionary<string, List<UIBehaviour>> controlDic = new Dictionary<string, List<UIBehaviour>>();


    protected virtual void Awake()
    {
        FindChildrenUIComponent<Button>();
        FindChildrenUIComponent<Slider>();
        FindChildrenUIComponent<Toggle>();
        FindChildrenUIComponent<Image>();
        FindChildrenUIComponent<Text>();
        FindChildrenUIComponent<ScrollRect>();
        FindChildrenUIComponent<InputField>();
    }
    /// <summary>
    /// 在子类重写显示逻辑
    /// </summary>
    public virtual void ShowMe() { }
    /// <summary>
    /// 隐藏
    /// </summary>
    public virtual void HideMe() { }
    /// <summary>
    /// 按钮监听事件，重写时自己根据传入的按钮名字处理对应按钮的逻辑
    /// </summary>
    /// <param name="btnName">按钮名字</param>
    protected virtual void Button_OnClick(string btnName) { }
    /// <summary>
    /// Toggle组件监听事件，也是根据传入的名字处理对应的事件
    /// </summary>
    /// <param name="toggleName">Toggle名字</param>
    /// <param name="value">Toggle的值</param>
    protected virtual void Toggle_OnValueChanged(string toggleName, bool value) { }
    /// <summary>
    /// Slider组件监听事件
    /// </summary>
    /// <param name="SliderName"></param>
    /// <param name="value"></param>
    protected virtual void Slider_OnValueChanged(string SliderName, float value) { }

    /// <summary>
    /// InputField组件监听输入值改变事件
    /// </summary>
    /// <param name="InputFieldName"></param>
    /// <param name="value"></param>
    protected virtual void InputField_OnValueChanged(string InputFieldName, string value) { }
    /// <summary>
    /// InputField组件监听输入提交事件
    /// </summary>
    /// <param name="InputFieldName"></param>
    /// <param name="value"></param>
    protected virtual void InputField_onSubmit(string InputFieldName, string value) { }
    /// <summary>
    /// InputField组件监听输入结束事件
    /// </summary>
    /// <param name="InputFieldName"></param>
    /// <param name="value"></param>
    protected virtual void InputField_onEndEdit(string InputFieldName, string value) { }

    /// <summary>
    /// 找到对应的UI组件并放入容器中
    /// </summary>
    /// <typeparam name="T"></typeparam>
    private void FindChildrenUIComponent<T>() where T : UIBehaviour
    {
        T[] controls = this.GetComponentsInChildren<T>();
        for (int i = 0; i < controls.Length; i++)
        {
            string objName = controls[i].gameObject.name;
            if (controlDic.ContainsKey(objName))
            {
                controlDic[objName].Add(controls[i]);
            }
            else
            {
                controlDic.Add(objName, new List<UIBehaviour>() { controls[i] });
            }
            //让Button、Toggle、Slider组件监听上事件
            if (controls[i] is Button)
            {
                (controls[i] as Button).onClick.AddListener(() =>
                {
                    Button_OnClick(objName);
                });
            }
            else if (controls[i] is Toggle)
            {
                (controls[i] as Toggle).onValueChanged.AddListener((isSelect) =>
                {
                    Toggle_OnValueChanged(objName, isSelect);
                });
            }
            else if (controls[i] is Slider)
            {
                (controls[i] as Slider).onValueChanged.AddListener((value) =>
                {
                    Slider_OnValueChanged(objName, value);
                });
            }
            else if (controls[i] is InputField)
            {
                (controls[i] as InputField).onValueChanged.AddListener((value) =>
                {
                    InputField_OnValueChanged(objName, value);
                    InputField_onSubmit(objName, value);
                    InputField_onEndEdit(objName, value);
                });
            }
        }
    }
    /// <summary>
    /// 根据组件挂载对象的名字获得对应UI组件
    /// </summary>
    /// <typeparam name="T">挂载对象的名字</typeparam>
    /// <param name="controlName">对应UI组件</param>
    /// <returns></returns>
    protected T GetControl<T>(string controlName) where T : UIBehaviour
    {
        if (controlDic.ContainsKey(controlName))
        {
            for (int i = 0; i < controlDic[controlName].Count; i++)
            {
                if (controlDic[controlName][i] is T)
                {
                    return controlDic[controlName][i] as T;
                }
            }
        }
        return null;
    }
}
