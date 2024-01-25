using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IPandaResLoad 
{
    /// <summary>
    /// 同步加载
    /// 根据泛型加载资源
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Respath"></param>
    /// <returns></returns>
    T Load<T>(string Respath) where T : UnityEngine.Object;

}
